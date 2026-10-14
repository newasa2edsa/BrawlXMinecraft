package dev.universalmodder.bslbridge;

import com.google.gson.JsonObject;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import net.minecraft.ChatFormatting;
import net.minecraft.core.particles.ParticleTypes;
import net.minecraft.network.chat.Component;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.sounds.SoundEvents;
import net.minecraft.sounds.SoundSource;
import net.minecraft.world.effect.MobEffectInstance;
import net.minecraft.world.effect.MobEffects;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.EntityTypes;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.ai.attributes.AttributeInstance;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.item.Item;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.phys.Vec3;

/**
 * Spawns and synchronizes custom Minecraft entities representing live BSL v68 brawlers.
 * Maps brawler selection (525), skin changes (506), level upgrades (520), star power / gadget
 * loadouts (532), emotes (538), health, super charge, hypercharge state, and arena movement.
 */
public final class BslEntityTracker {
	public static final String BRAWLER_TAG = "bsl_brawler";
	public static final String SUMMON_TAG = "bsl_summon";

	private static final Set<Integer> TANK_BRAWLERS = Set.of(2, 10, 18, 20, 24, 34, 51, 60, 62, 69, 71, 80, 104, 106);
	private static final Set<Integer> THROWER_BRAWLERS = Set.of(6, 9, 19, 22, 37, 48, 67, 77, 82, 87);
	private static final Set<Integer> ASSASSIN_BRAWLERS = Set.of(11, 12, 23, 43, 45, 49, 54, 70, 75, 76, 78, 81, 85, 86, 94, 95, 100, 107);

	public static final class TrackedBrawler {
		public final long playerId;
		public String playerName = "Brawler";
		public int brawlerId;
		public String brawlerName = "ShotgunGirl";
		public String displayName = "Shelly";
		public int skinId;
		public int level = 11;
		public int starPowerId;
		public int gadgetId;
		public int hp = 3900;
		public int maxHp = 3900;
		public int superCharge;
		public boolean hypercharged;
		public int bslX = 3150;
		public int bslY = 4950;
		public int bslZ = 0;
		public float yaw;
		public Mob entity;

		public TrackedBrawler(final long playerId) {
			this.playerId = playerId;
		}
	}

	private static final Map<Long, TrackedBrawler> tracked = new HashMap<>();

	private BslEntityTracker() {
	}

	public static int activeCount() {
		return tracked.size();
	}

	public static TrackedBrawler get(final long playerId) {
		return tracked.get(playerId);
	}

	public static void attach(final MinecraftServer server) {
		discardStaleEntities(server.overworld());
	}

	public static void detach(final MinecraftServer server) {
		for (TrackedBrawler tb : tracked.values()) {
			if (tb.entity != null && !tb.entity.isRemoved()) {
				tb.entity.discard();
			}
		}
		tracked.clear();
		discardStaleEntities(server.overworld());
	}

	public static void tick(final MinecraftServer server) {
		ServerLevel level = server.overworld();
		for (TrackedBrawler tb : tracked.values()) {
			if (tb.entity == null || tb.entity.isRemoved()) {
				continue;
			}
			// Emit continuous aura particles when Hypercharged or Super Ready
			if (tb.hypercharged) {
				level.sendParticles(ParticleTypes.SOUL_FIRE_FLAME,
					tb.entity.getX(), tb.entity.getY() + 1.0, tb.entity.getZ(),
					4, 0.35, 0.5, 0.35, 0.02);
				level.sendParticles(ParticleTypes.ELECTRIC_SPARK,
					tb.entity.getX(), tb.entity.getY() + 1.2, tb.entity.getZ(),
					3, 0.3, 0.4, 0.3, 0.05);
			} else if (tb.superCharge >= 100 && server.getTickCount() % 10 == 0) {
				level.sendParticles(ParticleTypes.END_ROD,
					tb.entity.getX(), tb.entity.getY() + 0.5, tb.entity.getZ(),
					2, 0.25, 0.2, 0.25, 0.01);
			}
		}
	}

	public static void onMatchState(final ServerLevel level, final JsonObject msg) {
		String state = msg.has("state") ? msg.get("state").getAsString() : "";
		long playerId = msg.has("playerId") ? msg.get("playerId").getAsLong() : 1L;
		String displayName = msg.has("displayName") ? msg.get("displayName").getAsString() : "Brawler";

		if ("start".equals(state)) {
			BslCoordinateMapper.resetAnchor();
			BslCoordinateMapper.ensureAnchoredToPlayer(level.getServer());
			Vec3 anchor = BslCoordinateMapper.getAnchor();
			level.sendParticles(ParticleTypes.TOTEM_OF_UNDYING, anchor.x, anchor.y + 1.0, anchor.z, 40, 1.5, 0.5, 1.5, 0.15);
			level.playSound(null, anchor.x, anchor.y, anchor.z, SoundEvents.RAID_HORN.value(), SoundSource.PLAYERS, 1.0F, 1.2F);
			broadcastChat(level.getServer(), String.format(Locale.ROOT,
				"§6[BSL v68] §eMatch started! Player #%d entered the arena as §b%s§e.", playerId, displayName));
		} else if ("end".equals(state)) {
			discardSummons(level);
			broadcastChat(level.getServer(), String.format(Locale.ROOT,
				"§6[BSL v68] §7Match ended for Player #%d (%s).", playerId, displayName));
		}
	}

	public static void onCharacterState(final ServerLevel level, final JsonObject msg) {
		long playerId = msg.has("playerId") ? msg.get("playerId").getAsLong() : 1L;
		boolean online = !msg.has("online") || msg.get("online").getAsBoolean();
		String action = msg.has("action") ? msg.get("action").getAsString() : "sync";

		if (!online || "disconnect".equals(action)) {
			TrackedBrawler removed = tracked.remove(playerId);
			if (removed != null && removed.entity != null && !removed.entity.isRemoved()) {
				level.sendParticles(ParticleTypes.POOF,
					removed.entity.getX(), removed.entity.getY() + 1.0, removed.entity.getZ(),
					20, 0.3, 0.5, 0.3, 0.05);
				level.playSound(null, removed.entity.getX(), removed.entity.getY(), removed.entity.getZ(),
					SoundEvents.ENDERMAN_TELEPORT, SoundSource.PLAYERS, 0.8F, 0.8F);
				removed.entity.discard();
			}
			return;
		}

		TrackedBrawler tb = tracked.computeIfAbsent(playerId, TrackedBrawler::new);
		int prevBrawlerId = tb.brawlerId;

		if (msg.has("playerName")) tb.playerName = msg.get("playerName").getAsString();
		if (msg.has("brawlerId")) tb.brawlerId = msg.get("brawlerId").getAsInt();
		if (msg.has("brawlerName")) tb.brawlerName = msg.get("brawlerName").getAsString();
		if (msg.has("displayName")) tb.displayName = msg.get("displayName").getAsString();
		if (msg.has("skinId")) tb.skinId = msg.get("skinId").getAsInt();
		if (msg.has("level")) tb.level = msg.get("level").getAsInt();
		if (msg.has("starPowerId")) tb.starPowerId = msg.get("starPowerId").getAsInt();
		if (msg.has("gadgetId")) tb.gadgetId = msg.get("gadgetId").getAsInt();
		if (msg.has("hp")) tb.hp = msg.get("hp").getAsInt();
		if (msg.has("maxHp")) tb.maxHp = msg.get("maxHp").getAsInt();
		if (msg.has("superCharge")) tb.superCharge = msg.get("superCharge").getAsInt();
		if (msg.has("hypercharged")) tb.hypercharged = msg.get("hypercharged").getAsBoolean();
		if (msg.has("x")) tb.bslX = msg.get("x").getAsInt();
		if (msg.has("y")) tb.bslY = msg.get("y").getAsInt();
		if (msg.has("z")) tb.bslZ = msg.get("z").getAsInt();
		if (msg.has("yaw")) tb.yaw = msg.get("yaw").getAsFloat();

		Vec3 mcPos = BslCoordinateMapper.toMinecraft(level, tb.bslX, tb.bslY, tb.bslZ);

		// If the brawler class changed (e.g., Tank <-> Marksman), respawn with the matching entity archetype
		if (tb.entity != null && !tb.entity.isRemoved() && archetypeFor(prevBrawlerId) != archetypeFor(tb.brawlerId)) {
			tb.entity.discard();
			tb.entity = null;
		}

		if (tb.entity == null || tb.entity.isRemoved()) {
			tb.entity = spawnBrawlerEntity(level, tb, mcPos);
		} else {
			tb.entity.snapTo(mcPos.x, mcPos.y, mcPos.z, tb.yaw, 0.0F);
			tb.entity.setYHeadRot(tb.yaw);
		}

		if (tb.entity != null) {
			syncEntityProperties(level, tb, action);
		}
	}

	public static void onMove(final ServerLevel level, final JsonObject msg) {
		long playerId = msg.has("playerId") ? msg.get("playerId").getAsLong() : 1L;
		TrackedBrawler tb = tracked.computeIfAbsent(playerId, TrackedBrawler::new);

		if (msg.has("brawlerId")) tb.brawlerId = msg.get("brawlerId").getAsInt();
		if (msg.has("brawlerName")) tb.brawlerName = msg.get("brawlerName").getAsString();
		if (msg.has("displayName")) tb.displayName = msg.get("displayName").getAsString();
		if (msg.has("x")) tb.bslX = msg.get("x").getAsInt();
		if (msg.has("y")) tb.bslY = msg.get("y").getAsInt();
		if (msg.has("z")) tb.bslZ = msg.get("z").getAsInt();
		if (msg.has("yaw")) tb.yaw = msg.get("yaw").getAsFloat();

		Vec3 mcPos = BslCoordinateMapper.toMinecraft(level, tb.bslX, tb.bslY, tb.bslZ);
		if (tb.entity == null || tb.entity.isRemoved()) {
			tb.entity = spawnBrawlerEntity(level, tb, mcPos);
			if (tb.entity != null) {
				syncEntityProperties(level, tb, "move");
			}
		} else {
			tb.entity.snapTo(mcPos.x, mcPos.y, mcPos.z, tb.yaw, 0.0F);
			tb.entity.setYHeadRot(tb.yaw);
			level.sendParticles(ParticleTypes.CLOUD, mcPos.x, mcPos.y + 0.1, mcPos.z, 1, 0.15, 0.02, 0.15, 0.01);
		}
	}

	private static EntityType<? extends Mob> archetypeFor(final int brawlerId) {
		if (TANK_BRAWLERS.contains(brawlerId)) {
			return EntityTypes.VINDICATOR;
		}
		if (THROWER_BRAWLERS.contains(brawlerId)) {
			return EntityTypes.WITCH;
		}
		if (ASSASSIN_BRAWLERS.contains(brawlerId)) {
			return EntityTypes.VINDICATOR;
		}
		return EntityTypes.PILLAGER;
	}

	private static Mob spawnBrawlerEntity(final ServerLevel level, final TrackedBrawler tb, final Vec3 pos) {
		EntityType<? extends Mob> type = archetypeFor(tb.brawlerId);
		Mob mob = type.create(level, EntitySpawnReason.COMMAND);
		if (mob == null) {
			return null;
		}

		mob.setNoAi(true);
		mob.setSilent(true);
		mob.setPersistenceRequired();
		mob.addTag(BRAWLER_TAG);
		mob.snapTo(pos.x, pos.y, pos.z, tb.yaw, 0.0F);
		mob.setYHeadRot(tb.yaw);

		if (!level.addFreshEntity(mob)) {
			return null;
		}

		level.sendParticles(ParticleTypes.PORTAL, pos.x, pos.y + 1.0, pos.z, 25, 0.4, 0.6, 0.4, 0.1);
		level.playSound(null, pos.x, pos.y, pos.z, SoundEvents.BEACON_ACTIVATE, SoundSource.PLAYERS, 0.7F, 1.4F);
		return mob;
	}

	private static void syncEntityProperties(final ServerLevel level, final TrackedBrawler tb, final String action) {
		Mob mob = tb.entity;

		// 1. Sync Max Health & Current Health (200 BSL HP = 1.0 Minecraft Health point)
		double mcMaxHp = Math.max(4.0, tb.maxHp / 200.0);
		double mcHp = Math.max(1.0, Math.min(mcMaxHp, tb.hp / 200.0));
		AttributeInstance maxHealthAttr = mob.getAttribute(Attributes.MAX_HEALTH);
		if (maxHealthAttr != null) {
			maxHealthAttr.setBaseValue(mcMaxHp);
		}
		mob.setHealth((float) mcHp);

		// 2. Sync Scale (Hypercharge enlarges the brawler entity by 15%)
		AttributeInstance scaleAttr = mob.getAttribute(Attributes.SCALE);
		if (scaleAttr != null) {
			scaleAttr.setBaseValue(tb.hypercharged ? 1.15 : 1.0);
		}

		// 3. Equip Weapon & Ability Items based on Brawler ID, Skin, Star Power, and Gadget
		Item weaponItem = selectWeaponItem(tb.brawlerId);
		mob.setItemSlot(EquipmentSlot.MAINHAND, new ItemStack(weaponItem));

		if (tb.gadgetId > 0) {
			mob.setItemSlot(EquipmentSlot.OFFHAND, new ItemStack(Items.TOTEM_OF_UNDYING));
		} else if (tb.starPowerId > 0) {
			mob.setItemSlot(EquipmentSlot.OFFHAND, new ItemStack(Items.END_CRYSTAL));
		} else {
			mob.setItemSlot(EquipmentSlot.OFFHAND, ItemStack.EMPTY);
		}

		Item helmet = selectSkinHelmet(tb.skinId, tb.level);
		mob.setItemSlot(EquipmentSlot.HEAD, helmet != null ? new ItemStack(helmet) : ItemStack.EMPTY);

		// 4. Sync Status Effects (Glowing when Super is ready or Hypercharged)
		if (tb.hypercharged || tb.superCharge >= 100) {
			mob.setGlowingTag(true);
			mob.addEffect(new MobEffectInstance(MobEffects.GLOWING, 200, 0, false, false));
		} else {
			mob.setGlowingTag(false);
			mob.removeEffect(MobEffects.GLOWING);
		}

		// 5. Update Custom Nameplate
		String badge = tb.hypercharged ? " §d§l[HYPERCHARGE]"
			: (tb.superCharge >= 100 ? " §e§l[SUPER READY]" : String.format(Locale.ROOT, " §7[%d%%]", tb.superCharge));
		String skinTag = tb.skinId > 0 ? String.format(Locale.ROOT, " §b(Skin #%d)", tb.skinId) : "";
		String title = String.format(Locale.ROOT, "§6[Lv.%d] §f%s §7- §a%s%s §c[%d/%d HP]%s",
			tb.level, tb.playerName, tb.displayName, skinTag, tb.hp, tb.maxHp, badge);

		mob.setCustomName(Component.literal(title).withStyle(ChatFormatting.WHITE));
		mob.setCustomNameVisible(true);

		// 6. Visual feedback on specific character state changes
		Vec3 p = mob.position();
		switch (action) {
			case "select_brawler" -> {
				level.sendParticles(ParticleTypes.ENCHANT, p.x, p.y + 1.2, p.z, 30, 0.4, 0.6, 0.4, 0.2);
				level.playSound(null, p.x, p.y, p.z, SoundEvents.ARMOR_EQUIP_DIAMOND.value(), SoundSource.PLAYERS, 1.0F, 1.1F);
			}
			case "change_skin" -> {
				level.sendParticles(ParticleTypes.HAPPY_VILLAGER, p.x, p.y + 1.2, p.z, 20, 0.4, 0.5, 0.4, 0.05);
				level.playSound(null, p.x, p.y, p.z, SoundEvents.AMETHYST_BLOCK_CHIME, SoundSource.PLAYERS, 1.0F, 1.3F);
			}
			case "upgrade_brawler" -> {
				level.sendParticles(ParticleTypes.TOTEM_OF_UNDYING, p.x, p.y + 1.0, p.z, 25, 0.4, 0.6, 0.4, 0.15);
				level.playSound(null, p.x, p.y, p.z, SoundEvents.PLAYER_LEVELUP, SoundSource.PLAYERS, 1.0F, 1.0F);
			}
			case "equip_starpower", "equip_gadget" -> {
				level.sendParticles(ParticleTypes.END_ROD, p.x, p.y + 1.2, p.z, 15, 0.3, 0.4, 0.3, 0.05);
				level.playSound(null, p.x, p.y, p.z, SoundEvents.ENCHANTMENT_TABLE_USE, SoundSource.PLAYERS, 0.9F, 1.2F);
			}
			case "select_emote", "emote" -> {
				level.sendParticles(ParticleTypes.NOTE, p.x, p.y + 2.2, p.z, 8, 0.3, 0.2, 0.3, 1.0);
				level.sendParticles(ParticleTypes.HEART, p.x, p.y + 2.0, p.z, 5, 0.25, 0.2, 0.25, 0.02);
				level.playSound(null, p.x, p.y, p.z, SoundEvents.NOTE_BLOCK_CHIME.value(), SoundSource.PLAYERS, 1.0F, 1.2F);
			}
		}
	}

	private static Item selectWeaponItem(final int brawlerId) {
		if (THROWER_BRAWLERS.contains(brawlerId)) {
			return Items.TNT;
		}
		if (ASSASSIN_BRAWLERS.contains(brawlerId)) {
			return brawlerId == 11 ? Items.IRON_SHOVEL : Items.NETHERITE_SWORD;
		}
		if (TANK_BRAWLERS.contains(brawlerId)) {
			return brawlerId == 20 ? Items.MACE : Items.DIAMOND_AXE;
		}
		if (brawlerId == 14 || brawlerId == 29 || brawlerId == 79) {
			return Items.BOW;
		}
		return Items.CROSSBOW;
	}

	private static Item selectSkinHelmet(final int skinId, final int level) {
		if (skinId <= 0) {
			return level >= 11 ? Items.GOLDEN_HELMET : null;
		}
		return switch (skinId % 4) {
			case 0 -> Items.DIAMOND_HELMET;
			case 1 -> Items.NETHERITE_HELMET;
			case 2 -> Items.GOLDEN_HELMET;
			default -> Items.TURTLE_HELMET;
		};
	}

	public static void discardSummons(final ServerLevel level) {
		List<Entity> toRemove = new ArrayList<>();
		for (Entity e : level.getAllEntities()) {
			if (e.entityTags().contains(SUMMON_TAG)) {
				toRemove.add(e);
			}
		}
		toRemove.forEach(Entity::discard);
	}

	private static void discardStaleEntities(final ServerLevel level) {
		List<Entity> stale = new ArrayList<>();
		for (Entity e : level.getAllEntities()) {
			if (e.entityTags().contains(BRAWLER_TAG) || e.entityTags().contains(SUMMON_TAG)) {
				stale.add(e);
			}
		}
		stale.forEach(Entity::discard);
	}

	private static void broadcastChat(final MinecraftServer server, final String text) {
		for (var player : server.getPlayerList().getPlayers()) {
			player.sendSystemMessage(Component.literal(text));
		}
	}
}
