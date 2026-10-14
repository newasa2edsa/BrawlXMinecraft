package dev.universalmodder.bslbridge;

import com.google.gson.JsonObject;
import java.util.List;
import java.util.Locale;
import net.minecraft.core.particles.ParticleOptions;
import net.minecraft.core.particles.ParticleTypes;
import net.minecraft.network.chat.Component;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.sounds.SoundEvents;
import net.minecraft.sounds.SoundSource;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.effect.MobEffectInstance;
import net.minecraft.world.effect.MobEffects;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.EntityTypes;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.animal.polarbear.PolarBear;
import net.minecraft.world.entity.animal.golem.IronGolem;
import net.minecraft.world.level.Level;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;

/**
 * Maps live BSL v68 combat events (basic attacks, Super abilities, Gadgets, and Hypercharges)
 * into Minecraft particle trails, positional sound effects, area damage, summons, and explosions.
 */
public final class BslEffectMapper {
	private BslEffectMapper() {
	}

	/**
	 * Handles BSL basic attack ("attack") events:
	 * - Swings the brawler entity's weapon and rotates toward target coordinates.
	 * - Ray-marches weapon-specific particles from (x, y, z) to (targetX, targetY, targetZ).
	 * - Plays weapon audio and triggers impact effects / explosions at the target coordinates.
	 */
	public static void onAttack(final ServerLevel level, final JsonObject msg) {
		long playerId = msg.has("playerId") ? msg.get("playerId").getAsLong() : 1L;
		int brawlerId = msg.has("brawlerId") ? msg.get("brawlerId").getAsInt() : 0;
		String behavior = msg.has("behavior") ? msg.get("behavior").getAsString() : "Attack";
		int damage = msg.has("damage") ? msg.get("damage").getAsInt() : 1000;
		boolean explosive = msg.has("explosive") && msg.get("explosive").getAsBoolean();

		int x = msg.has("x") ? msg.get("x").getAsInt() : 3150;
		int y = msg.has("y") ? msg.get("y").getAsInt() : 4950;
		int z = msg.has("z") ? msg.get("z").getAsInt() : 0;
		int tx = msg.has("targetX") ? msg.get("targetX").getAsInt() : x + 900;
		int ty = msg.has("targetY") ? msg.get("targetY").getAsInt() : y;
		int tz = msg.has("targetZ") ? msg.get("targetZ").getAsInt() : 0;

		Vec3 start = BslCoordinateMapper.toMinecraft(level, x, y, z);
		Vec3 end = BslCoordinateMapper.toMinecraft(level, tx, ty, tz);
		float yaw = BslCoordinateMapper.toMinecraftYaw(tx - x, ty - y, 0.0F);

		BslEntityTracker.TrackedBrawler tracked = BslEntityTracker.get(playerId);
		Mob attacker = tracked != null ? tracked.entity : null;
		if (attacker != null && !attacker.isRemoved()) {
			if ("Charge".equals(behavior)) {
				attacker.snapTo(end.x, end.y, end.z, yaw, 0.0F);
			} else {
				attacker.setYRot(yaw);
				attacker.setYHeadRot(yaw);
			}
			attacker.swing(InteractionHand.MAIN_HAND, net.minecraft.world.item.component.SwingAnimation.DEFAULT);
		}

		// Select particle & sound archetype for the brawler's weapon
		ParticleOptions trailParticle = selectAttackParticle(brawlerId, behavior, explosive);
		rayMarchParticles(level, trailParticle, start.add(0.0, 1.1, 0.0), end.add(0.0, 1.0, 0.0), 0.45);

		if (explosive) {
			level.playSound(null, start.x, start.y, start.z, SoundEvents.FIREWORK_ROCKET_LAUNCH, SoundSource.PLAYERS, 1.0F, 0.9F);
			// Trigger non-block-destroying explosion at target impact coordinates
			level.explode(attacker, end.x, end.y + 0.5, end.z, 2.0F, false, Level.ExplosionInteraction.NONE);
			level.sendParticles(ParticleTypes.EXPLOSION, end.x, end.y + 0.5, end.z, 6, 0.4, 0.4, 0.4, 0.05);
		} else if ("Charge".equals(behavior)) {
			level.playSound(null, end.x, end.y, end.z, SoundEvents.PLAYER_ATTACK_SWEEP, SoundSource.PLAYERS, 1.2F, 0.85F);
			level.sendParticles(ParticleTypes.SWEEP_ATTACK, end.x, end.y + 1.0, end.z, 4, 0.5, 0.2, 0.5, 0.0);
		} else if (brawlerId == 0 || brawlerId == 2 || brawlerId == 18 || brawlerId == 50) {
			// Shotgun spread burst
			level.playSound(null, start.x, start.y, start.z, SoundEvents.GENERIC_EXPLODE.value(), SoundSource.PLAYERS, 0.75F, 1.6F);
			level.sendParticles(ParticleTypes.CRIT, end.x, end.y + 1.0, end.z, 18, 0.8, 0.4, 0.8, 0.15);
			level.sendParticles(ParticleTypes.CAMPFIRE_COSY_SMOKE, start.x, start.y + 1.1, start.z, 5, 0.2, 0.2, 0.2, 0.02);
		} else {
			level.playSound(null, start.x, start.y, start.z, SoundEvents.CROSSBOW_SHOOT, SoundSource.PLAYERS, 1.0F, 1.15F);
			level.sendParticles(ParticleTypes.CRIT, end.x, end.y + 1.0, end.z, 10, 0.3, 0.3, 0.3, 0.1);
		}

		applyAreaDamage(level, attacker, end, 2.5, Math.max(2.0F, damage / 200.0F));
	}

	/**
	 * Handles BSL Super / Ultimate ("super") events:
	 * - Explosive Supers detonate at target coordinates with shockwave rings.
	 * - Charge / FlyJump / Blink Supers leap/dash the synced brawler entity to the target and slam the ground.
	 * - Summoner Supers spawn custom companion entities (e.g. Nita's Bruce bear, Jessie's Scrappy golem).
	 * - AreaBuff Supers emit healing/buff rings.
	 */
	public static void onSuper(final ServerLevel level, final JsonObject msg) {
		long playerId = msg.has("playerId") ? msg.get("playerId").getAsLong() : 1L;
		int brawlerId = msg.has("brawlerId") ? msg.get("brawlerId").getAsInt() : 0;
		String displayName = msg.has("displayName") ? msg.get("displayName").getAsString() : "Brawler";
		String behavior = msg.has("behavior") ? msg.get("behavior").getAsString() : "Attack";
		int damage = msg.has("damage") ? msg.get("damage").getAsInt() : 2000;
		boolean explosive = !msg.has("explosive") || msg.get("explosive").getAsBoolean();
		String summon = msg.has("summon") ? msg.get("summon").getAsString() : "";

		int x = msg.has("x") ? msg.get("x").getAsInt() : 3150;
		int y = msg.has("y") ? msg.get("y").getAsInt() : 4950;
		int z = msg.has("z") ? msg.get("z").getAsInt() : 0;
		int tx = msg.has("targetX") ? msg.get("targetX").getAsInt() : x + 1500;
		int ty = msg.has("targetY") ? msg.get("targetY").getAsInt() : y;
		int tz = msg.has("targetZ") ? msg.get("targetZ").getAsInt() : 0;

		Vec3 start = BslCoordinateMapper.toMinecraft(level, x, y, z);
		Vec3 end = BslCoordinateMapper.toMinecraft(level, tx, ty, tz);
		float yaw = BslCoordinateMapper.toMinecraftYaw(tx - x, ty - y, 0.0F);

		BslEntityTracker.TrackedBrawler tracked = BslEntityTracker.get(playerId);
		Mob attacker = tracked != null ? tracked.entity : null;

		// Charge / Leap / Teleport Supers move the synced entity to the landing coordinates
		if ("Charge".equals(behavior) || "FlyJump".equals(behavior) || "Blink".equals(behavior)) {
			rayMarchParticles(level, ParticleTypes.FLAME, start.add(0.0, 1.0, 0.0), end.add(0.0, 1.0, 0.0), 0.35);
			rayMarchParticles(level, ParticleTypes.CLOUD, start.add(0.0, 0.2, 0.0), end.add(0.0, 0.2, 0.0), 0.4);
			if (attacker != null && !attacker.isRemoved()) {
				attacker.snapTo(end.x, end.y, end.z, yaw, 0.0F);
				attacker.setYHeadRot(yaw);
				attacker.swing(InteractionHand.MAIN_HAND, net.minecraft.world.item.component.SwingAnimation.DEFAULT);
			}
			level.playSound(null, end.x, end.y, end.z, SoundEvents.GENERIC_EXPLODE.value(), SoundSource.PLAYERS, 1.3F, 0.75F);
			level.explode(attacker, end.x, end.y + 0.2, end.z, 3.0F, false, Level.ExplosionInteraction.NONE);
			spawnShockwaveRing(level, ParticleTypes.EXPLOSION, end.add(0.0, 0.3, 0.0), 3.5, 24);
			applyAreaDamage(level, attacker, end, 4.0, Math.max(6.0F, damage / 180.0F));
			return;
		}

		// Summoner Supers (e.g. Nita -> Bruce Bear, Jessie/Pam/Penny/Larry -> Turret/Companion Golem)
		if (!summon.isEmpty() || brawlerId == 7 || brawlerId == 8 || brawlerId == 16 || brawlerId == 19 || brawlerId == 31 || brawlerId == 77) {
			rayMarchParticles(level, ParticleTypes.END_ROD, start.add(0.0, 1.2, 0.0), end.add(0.0, 1.0, 0.0), 0.4);
			spawnBrawlerSummon(level, brawlerId, displayName, summon, end, yaw);
			level.playSound(null, end.x, end.y, end.z, SoundEvents.EVOKER_PREPARE_SUMMON, SoundSource.PLAYERS, 1.2F, 1.0F);
			return;
		}

		// Support / AreaBuff / Status Supers (e.g. Poco, Sandy, Max, Gene, Tara)
		if ("AreaBuff".equals(behavior) || "Invisibility".equals(behavior) || "Shield".equals(behavior) || brawlerId == 13 || brawlerId == 28 || brawlerId == 32) {
			spawnShockwaveRing(level, ParticleTypes.HEART, end.add(0.0, 1.0, 0.0), 4.5, 32);
			spawnShockwaveRing(level, ParticleTypes.TOTEM_OF_UNDYING, end.add(0.0, 0.5, 0.0), 4.5, 32);
			level.playSound(null, end.x, end.y, end.z, SoundEvents.TOTEM_USE, SoundSource.PLAYERS, 1.0F, 1.15F);
			if (attacker != null && !attacker.isRemoved()) {
				attacker.addEffect(new MobEffectInstance(MobEffects.REGENERATION, 120, 1));
				attacker.addEffect(new MobEffectInstance(MobEffects.SPEED, 120, 1));
			}
			return;
		}

		// Standard Offensive / Explosive Super Barrage (Shelly Super Shell, Brock Rocket Rain, Dynamike Big Barrel, Frank Stunning Blow)
		rayMarchParticles(level, ParticleTypes.SONIC_BOOM, start.add(0.0, 1.2, 0.0), end.add(0.0, 1.0, 0.0), 0.9);
		rayMarchParticles(level, ParticleTypes.FLAME, start.add(0.0, 1.0, 0.0), end.add(0.0, 1.0, 0.0), 0.35);
		level.playSound(null, start.x, start.y, start.z, SoundEvents.WARDEN_SONIC_BOOM, SoundSource.PLAYERS, 1.1F, 1.1F);

		float radius = explosive ? 3.5F : 2.5F;
		level.explode(attacker, end.x, end.y + 0.5, end.z, radius, false, Level.ExplosionInteraction.NONE);
		level.sendParticles(ParticleTypes.EXPLOSION_EMITTER, end.x, end.y + 0.5, end.z, 2, 0.5, 0.5, 0.5, 0.0);
		spawnShockwaveRing(level, ParticleTypes.FLAME, end.add(0.0, 0.3, 0.0), radius, 28);
		applyAreaDamage(level, attacker, end, radius + 1.0, Math.max(8.0F, damage / 160.0F));
	}

	/**
	 * Handles BSL Gadget & Hypercharge ("ability") events.
	 */
	public static void onAbility(final ServerLevel level, final JsonObject msg) {
		long playerId = msg.has("playerId") ? msg.get("playerId").getAsLong() : 1L;
		String subType = msg.has("subType") ? msg.get("subType").getAsString() : "gadget";
		int x = msg.has("x") ? msg.get("x").getAsInt() : 3150;
		int y = msg.has("y") ? msg.get("y").getAsInt() : 4950;
		int z = msg.has("z") ? msg.get("z").getAsInt() : 0;

		Vec3 pos = BslCoordinateMapper.toMinecraft(level, x, y, z);
		BslEntityTracker.TrackedBrawler tracked = BslEntityTracker.get(playerId);
		Mob mob = tracked != null ? tracked.entity : null;
		if (mob != null && !mob.isRemoved()) {
			pos = mob.position();
		}

		if ("hypercharge".equals(subType)) {
			if (tracked != null) {
				tracked.hypercharged = true;
			}
			spawnShockwaveRing(level, ParticleTypes.SOUL_FIRE_FLAME, pos.add(0.0, 0.5, 0.0), 3.0, 28);
			level.sendParticles(ParticleTypes.REVERSE_PORTAL, pos.x, pos.y + 1.0, pos.z, 40, 0.6, 0.8, 0.6, 0.06);
			level.sendParticles(ParticleTypes.ELECTRIC_SPARK, pos.x, pos.y + 1.2, pos.z, 35, 0.6, 0.8, 0.6, 0.12);
			level.playSound(null, pos.x, pos.y, pos.z, SoundEvents.TRIDENT_THUNDER.value(), SoundSource.PLAYERS, 1.2F, 1.15F);
			if (mob != null && !mob.isRemoved()) {
				mob.addEffect(new MobEffectInstance(MobEffects.SPEED, 200, 1));
				mob.addEffect(new MobEffectInstance(MobEffects.STRENGTH, 200, 1));
				mob.addEffect(new MobEffectInstance(MobEffects.GLOWING, 200, 0));
			}
		} else {
			// Gadget activation
			spawnShockwaveRing(level, ParticleTypes.TOTEM_OF_UNDYING, pos.add(0.0, 0.8, 0.0), 2.2, 20);
			level.sendParticles(ParticleTypes.ENCHANTED_HIT, pos.x, pos.y + 1.2, pos.z, 25, 0.4, 0.5, 0.4, 0.1);
			level.playSound(null, pos.x, pos.y, pos.z, SoundEvents.ILLUSIONER_CAST_SPELL, SoundSource.PLAYERS, 1.0F, 1.2F);
		}
	}

	private static ParticleOptions selectAttackParticle(final int brawlerId, final String behavior, final boolean explosive) {
		if (brawlerId == 40) {
			return ParticleTypes.FLAME; // Amber FireDude
		}
		if (brawlerId == 41 || brawlerId == 35) {
			return ParticleTypes.SNOWFLAKE; // Lou / Gale
		}
		if (brawlerId == 7 || brawlerId == 46 || brawlerId == 65) {
			return ParticleTypes.ELECTRIC_SPARK; // Jessie / Belle / Mandy
		}
		if (brawlerId == 8 || brawlerId == 20 || brawlerId == 34) {
			return ParticleTypes.SONIC_BOOM; // Nita / Frank / Jacky shockwaves
		}
		if (explosive) {
			return ParticleTypes.LAVA;
		}
		if ("Charge".equals(behavior)) {
			return ParticleTypes.CLOUD;
		}
		return ParticleTypes.CRIT;
	}

	private static void rayMarchParticles(final ServerLevel level, final ParticleOptions particle, final Vec3 from, final Vec3 to, final double step) {
		Vec3 diff = to.subtract(from);
		double dist = diff.length();
		if (dist < 1.0E-3) {
			level.sendParticles(particle, from.x, from.y, from.z, 4, 0.1, 0.1, 0.1, 0.02);
			return;
		}

		Vec3 dir = diff.scale(1.0 / dist);
		int steps = Math.min(48, (int) Math.ceil(dist / step));
		for (int i = 0; i <= steps; i++) {
			Vec3 p = from.add(dir.scale(i * step));
			level.sendParticles(particle, p.x, p.y, p.z, 2, 0.08, 0.08, 0.08, 0.01);
		}
	}

	private static void spawnShockwaveRing(final ServerLevel level, final ParticleOptions particle, final Vec3 center, final double radius, final int points) {
		for (int i = 0; i < points; i++) {
			double angle = (2.0 * Math.PI * i) / points;
			double px = center.x + Math.cos(angle) * radius;
			double pz = center.z + Math.sin(angle) * radius;
			level.sendParticles(particle, px, center.y, pz, 2, 0.1, 0.1, 0.1, 0.02);
		}
	}

	private static void spawnBrawlerSummon(final ServerLevel level, final int brawlerId, final String ownerName, final String summonCode, final Vec3 pos, final float yaw) {
		Mob summoned;
		String label;
		if (brawlerId == 8 || summonCode.contains("ShamanPet")) {
			PolarBear bear = EntityTypes.POLAR_BEAR.create(level, EntitySpawnReason.COMMAND);
			summoned = bear;
			label = String.format(Locale.ROOT, "§c[BSL Summon] §f%s's Bruce Bear", ownerName);
		} else {
			IronGolem golem = EntityTypes.IRON_GOLEM.create(level, EntitySpawnReason.COMMAND);
			summoned = golem;
			label = String.format(Locale.ROOT, "§e[BSL Turret] §f%s's %s", ownerName, summonCode.isEmpty() ? "Scrappy" : summonCode);
		}

		if (summoned == null) {
			return;
		}

		summoned.setPersistenceRequired();
		summoned.addTag(BslEntityTracker.SUMMON_TAG);
		summoned.snapTo(pos.x, pos.y, pos.z, yaw, 0.0F);
		summoned.setCustomName(Component.literal(label));
		summoned.setCustomNameVisible(true);

		if (level.addFreshEntity(summoned)) {
			level.sendParticles(ParticleTypes.TOTEM_OF_UNDYING, pos.x, pos.y + 1.0, pos.z, 30, 0.4, 0.6, 0.4, 0.12);
		}
	}

	private static void applyAreaDamage(final ServerLevel level, final Mob attacker, final Vec3 center, final double radius, final float amount) {
		AABB box = new AABB(
			center.x - radius, center.y - 2.0, center.z - radius,
			center.x + radius, center.y + 3.0, center.z + radius
		);
		List<LivingEntity> targets = level.getEntitiesOfClass(LivingEntity.class, box,
			e -> e != attacker && !e.entityTags().contains(BslEntityTracker.BRAWLER_TAG));

		for (LivingEntity target : targets) {
			target.hurtServer(level, attacker != null ? level.damageSources().mobAttack(attacker) : level.damageSources().generic(), amount);
		}
	}
}
