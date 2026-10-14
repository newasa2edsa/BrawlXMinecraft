package dev.rehan.passthrough;

import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import net.minecraft.core.BlockPos;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.entity.projectile.FireworkRocketEntity;
import net.minecraft.world.entity.projectile.Projectile;
import net.minecraft.world.entity.projectile.arrow.AbstractArrow;
import net.minecraft.world.entity.projectile.hurtingprojectile.Fireball;
import net.minecraft.world.entity.projectile.hurtingprojectile.LargeFireball;
import net.minecraft.world.entity.projectile.throwableitemprojectile.ThrowableItemProjectile;
import net.minecraft.world.entity.projectile.throwableitemprojectile.ThrownEnderpearl;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.phys.Vec3;

/** Server-side half: the host's collision as invisible barrier blocks, commands, and events back to the host. */
public final class WorldBridge {
	private static volatile MinecraftServer server;
	/** Barriers we placed (so a reset only removes ours, never the player's builds). */
	private static final Set<BlockPos> barriers = ConcurrentHashMap.newKeySet();
	/** Block changes this tick (server thread only): true = now solid, false = gone. Sent at the end of the tick. */
	private static final Map<BlockPos, Boolean> changes = new LinkedHashMap<>();
	/** While placing or removing the host's own ground: those changes aren't news to the host (server thread only). */
	private static boolean placingGround;
	private static volatile double hostX, hostFeetY = 64.0, hostZ;
	private static volatile float hostYaw, hostPitch;
	private static volatile boolean hasHostPose;
	private static volatile long ignoreHostPoseUntilNanos;

	private WorldBridge() {
	}

	public static void updatePlayerPose(final double x, final double feetY, final double z, final float yaw, final float pitch) {
		if (System.nanoTime() >= ignoreHostPoseUntilNanos) {
			hostX = x;
			hostFeetY = feetY;
			hostZ = z;
		}
		hostYaw = yaw;
		hostPitch = pitch;
		hasHostPose = true;
	}

	public static void onPlayerTeleported(final double x, final double feetY, final double z) {
		hostX = x;
		hostFeetY = feetY;
		hostZ = z;
		ignoreHostPoseUntilNanos = System.nanoTime() + 400_000_000L;
	}

	public static boolean isTeleportIgnored() {
		return System.nanoTime() < ignoreHostPoseUntilNanos;
	}

	public static void shootFireball() {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}
		s.execute(() -> {
			if (s.getPlayerList().getPlayers().isEmpty()) {
				return;
			}
			ServerPlayer sp = s.getPlayerList().getPlayers().get(0);
			ServerLevel level = s.overworld();
			Vec3 look = sp.getLookAngle();
			LargeFireball fb = new LargeFireball(level, sp, look.scale(1.5), 3);
			fb.setPos(sp.getX() + look.x * 1.2, sp.getEyeY() - 0.1 + look.y * 1.2, sp.getZ() + look.z * 1.2);
			level.addFreshEntity(fb);
		});
	}

	/** Called when the player presses their Attack key/button: detonates any End Crystal or charged Respawn Anchor in front of Steve! */
	public static void onPlayerAttack() {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}
		s.execute(() -> {
			if (s.getPlayerList().getPlayers().isEmpty()) {
				return;
			}
			ServerPlayer sp = s.getPlayerList().getPlayers().get(0);
			ServerLevel level = s.overworld();
			Vec3 eye = sp.getEyePosition();
			Vec3 look = sp.getLookAngle();

			// 1. Check for End Crystals in front of Steve (up to 8.5 blocks away) and detonate them immediately!
			net.minecraft.world.entity.boss.enderdragon.EndCrystal targetCrystal = null;
			double bestDist = 8.5;
			for (Entity e : level.getAllEntities()) {
				if (e instanceof net.minecraft.world.entity.boss.enderdragon.EndCrystal crystal && crystal.isAlive()) {
					Vec3 to = crystal.position().add(0.0, 0.9, 0.0).subtract(eye);
					double d = to.length();
					if (d <= bestDist && d > 0.05) {
						double dot = to.normalize().dot(look);
						if (dot >= 0.65 || d <= 2.4) {
							bestDist = d;
							targetCrystal = crystal;
						}
					}
				}
			}
			if (targetCrystal != null) {
				double cx = targetCrystal.getX();
				double cy = targetCrystal.getY();
				double cz = targetCrystal.getZ();
				targetCrystal.discard();
				level.explode(null, cx, cy, cz, 6.0F, net.minecraft.world.level.Level.ExplosionInteraction.BLOCK);
				return;
			}

			// 2. Also check if attacking a charged Respawn Anchor in front of Steve -> detonate it!
			for (double step = 1.0; step <= 6.5; step += 0.6) {
				BlockPos bp = BlockPos.containing(eye.add(look.scale(step)));
				BlockState st = level.getBlockState(bp);
				if (st.is(Blocks.RESPAWN_ANCHOR) && st.getValue(net.minecraft.world.level.block.RespawnAnchorBlock.CHARGE) > 0) {
					level.removeBlock(bp, false);
					Vec3 center = Vec3.atCenterOf(bp);
					level.explode(null, level.damageSources().badRespawnPointExplosion(center), null, center.x, center.y, center.z, 5.0F, true, net.minecraft.world.level.Level.ExplosionInteraction.BLOCK);
					return;
				}
			}
		});
	}

	/** Called when the player presses their Use key/button: handles End Crystal placement on any floor, Respawn Anchor charging/explosion, Chorus Fruit, Fishing Rod, and Anvil! */
	public static void onPlayerUseItem(final String itemPath) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}
		s.execute(() -> {
			if (s.getPlayerList().getPlayers().isEmpty()) {
				return;
			}
			ServerPlayer sp = s.getPlayerList().getPlayers().get(0);
			ServerLevel level = s.overworld();
			Vec3 eye = sp.getEyePosition();
			Vec3 look = sp.getLookAngle();

			// First check if the player is looking at a Respawn Anchor within 6.5 blocks
			BlockPos lookAnchor = null;
			for (double step = 1.0; step <= 6.5; step += 0.5) {
				BlockPos bp = BlockPos.containing(eye.add(look.scale(step)));
				if (level.getBlockState(bp).is(Blocks.RESPAWN_ANCHOR)) {
					lookAnchor = bp;
					break;
				}
			}
			if (lookAnchor == null) {
				// Also check y=64 directly in front of Steve
				BlockPos front = BlockPos.containing(sp.getX() + look.x * 2.5, 64.0, sp.getZ() + look.z * 2.5);
				for (int dx = -1; dx <= 1 && lookAnchor == null; dx++) {
					for (int dz = -1; dz <= 1; dz++) {
						BlockPos cand = front.offset(dx, 0, dz);
						if (level.getBlockState(cand).is(Blocks.RESPAWN_ANCHOR)) {
							lookAnchor = cand;
							break;
						}
					}
				}
			}

			if (lookAnchor != null) {
				BlockState st = level.getBlockState(lookAnchor);
				int charge = st.getValue(net.minecraft.world.level.block.RespawnAnchorBlock.CHARGE);
				if (itemPath.equals("glowstone") && charge < 4) {
					level.setBlock(lookAnchor, st.setValue(net.minecraft.world.level.block.RespawnAnchorBlock.CHARGE, charge + 1), Block.UPDATE_ALL);
					level.playSound(null, lookAnchor.getX() + 0.5, lookAnchor.getY() + 0.5, lookAnchor.getZ() + 0.5,
						net.minecraft.sounds.SoundEvents.RESPAWN_ANCHOR_CHARGE, net.minecraft.sounds.SoundSource.BLOCKS, 1.0F, 1.0F);
					return;
				} else if (charge > 0) {
					// Clicking a charged Respawn Anchor with anything other than Glowstone (or at 4 charges) detonates it!
					level.removeBlock(lookAnchor, false);
					Vec3 center = Vec3.atCenterOf(lookAnchor);
					level.explode(null, level.damageSources().badRespawnPointExplosion(center), null, center.x, center.y, center.z, 5.0F, true, net.minecraft.world.level.Level.ExplosionInteraction.BLOCK);
					return;
				}
			}

			// Compute floor placement position in front of Steve
			double placeDist = 2.8;
			if (look.y < -0.15) {
				double tFloor = (64.0 - eye.y) / look.y;
				if (tFloor >= 1.0 && tFloor <= 10.0) {
					placeDist = tFloor;
				}
			}
			double tx = Math.floor(sp.getX() + look.x * placeDist) + 0.5;
			double tz = Math.floor(sp.getZ() + look.z * placeDist) + 0.5;
			BlockPos targetBlock = BlockPos.containing(tx, 64.0, tz);

			switch (itemPath) {
				case "end_crystal" -> {
					// Place a floating End Crystal on the arena floor (or on top of a block at targetBlock) even without Obsidian!
					double cy = level.getBlockState(targetBlock).isAir() ? 64.0 : 65.0;
					for (Entity e : level.getAllEntities()) {
						if (e instanceof net.minecraft.world.entity.boss.enderdragon.EndCrystal c && c.position().distanceToSqr(tx, cy, tz) < 1.2) {
							return;
						}
					}
					net.minecraft.world.entity.boss.enderdragon.EndCrystal crystal =
						new net.minecraft.world.entity.boss.enderdragon.EndCrystal(level, tx, cy, tz);
					crystal.setShowBottom(false);
					level.addFreshEntity(crystal);
				}
				case "respawn_anchor" -> {
					if (level.getBlockState(targetBlock).isAir()) {
						level.setBlock(targetBlock, Blocks.RESPAWN_ANCHOR.defaultBlockState(), Block.UPDATE_ALL);
					}
				}
				case "chorus_fruit" -> {
					java.util.concurrent.ThreadLocalRandom rng = java.util.concurrent.ThreadLocalRandom.current();
					double nx = Math.clamp(sp.getX() + rng.nextDouble(-8.0, 8.0), -18.0, 18.0);
					double nz = Math.clamp(sp.getZ() + rng.nextDouble(-8.0, 8.0), -22.0, 22.0);
					onPlayerTeleported(nx, 64.0, nz);
					level.playSound(null, nx, 64.0, nz, net.minecraft.sounds.SoundEvents.CHORUS_FRUIT_TELEPORT, net.minecraft.sounds.SoundSource.PLAYERS, 1.0F, 1.0F);
					Passthrough.events.accept(String.format(Locale.ROOT, "{\"t\":\"pearl\",\"pos\":[%.3f,64.0,%.3f]}", nx, nz));
				}
				case "fishing_rod" -> {
					Passthrough.events.accept("{\"t\":\"fish_pull\"}");
				}
				case "anvil" -> {
					BlockPos skyPos = BlockPos.containing(tx, 70.0, tz);
					if (level.getBlockState(skyPos).isAir()) {
						level.setBlock(skyPos, Blocks.ANVIL.defaultBlockState(), Block.UPDATE_ALL);
					}
					Passthrough.events.accept(String.format(Locale.ROOT, "{\"t\":\"anvil_drop\",\"pos\":[%.3f,64.0,%.3f]}", tx, tz));
				}
				default -> {
				}
			}
		});
	}

	static void attach(final MinecraftServer s) {
		server = s;
	}

	static void detach() {
		server = null;
		barriers.clear();
		hasHostPose = false;
	}

	public static boolean ready() {
		return server != null;
	}

	static MinecraftServer server() {
		return server;
	}

	/** Columns of solid ground from the host: {x, z, yBottom, yTop, ...} in block coordinates (inclusive). Only air is replaced. */
	public static void solid(final int[] columns) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			BlockState barrier = Blocks.BARRIER.defaultBlockState();
			BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
			placingGround = true;
			for (int i = 0; i + 3 < columns.length; i += 4) {
				for (int y = columns[i + 2]; y <= columns[i + 3]; y++) {
					pos.set(columns[i], y, columns[i + 1]);
					if (level.isInWorldBounds(pos) && level.getBlockState(pos).isAir()) {
						level.setBlock(pos, barrier, Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE);
						barriers.add(pos.immutable());
					}
				}
			}

			placingGround = false;
		});
	}

	/** Remove every barrier we placed (e.g. when the host teleports somewhere else). */
	public static void clearSolid() {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			placingGround = true;
			for (BlockPos pos : barriers) {
				if (level.getBlockState(pos).is(Blocks.BARRIER)) {
					level.setBlock(pos, Blocks.AIR.defaultBlockState(), Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE);
				}
			}

			placingGround = false;
			barriers.clear();
		});
	}

	/** Run a command as the server (op). Results go to the log, not to chat (send_command_feedback is off). */
	public static void command(final String command) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			Passthrough.LOG.info("command: {}", command);
			s.getCommands().performPrefixedCommand(s.createCommandSourceStack(), command);
		});
	}

	private static boolean solidForHost(final ServerLevel level, final BlockPos pos, final BlockState state) {
		// the Nether's ground is the host's own ground turned: nothing to collide with that isn't there already
		return !state.isAir() && !state.is(Blocks.BARRIER) && !state.getCollisionShape(level, pos).isEmpty() && !Nether.isGround(pos);
	}

	/** Arrows the host already hit something with (they stay where they hit and aren't reported again). */
	private static final String HIT_TAG = "passthrough_hit";

	/** Every server tick: sync ServerPlayer pose, block changes, and projectiles in flight. */
	static void tick(final MinecraftServer s) {
		if (Passthrough.active && hasHostPose && !s.getPlayerList().getPlayers().isEmpty()) {
			ServerPlayer sp = s.getPlayerList().getPlayers().get(0);
			sp.setPos(hostX, hostFeetY, hostZ);
			sp.xo = sp.xOld = hostX;
			sp.yo = sp.yOld = hostFeetY;
			sp.zo = sp.zOld = hostZ;
			sp.setYRot(hostYaw);
			sp.setXRot(hostPitch);
			sp.yRotO = hostYaw;
			sp.xRotO = hostPitch;
			sp.yHeadRot = sp.yHeadRotO = hostYaw;
			if (sp.connection != null) {
				sp.connection.resetPosition();
			}
		}
		flush(s);
		if (Passthrough.active) {
			reportProjectiles(s.overworld());
		}

		MobWar.tick(s);
		Nether.tick(s);
	}

	/**
	 * Steve's arrows (bow, crossbow, trident), crossbow fireworks, fireballs, wind charges, and thrown items in flight:
	 * {"t":"proj","p":[[id,kind,x,y,z],...]}.
	 */
	private static void reportProjectiles(final ServerLevel level) {
		StringBuilder b = null;
		for (Entity e : level.getAllEntities()) {
			String kind = null;
			if (!(e instanceof Projectile projectile) || !(projectile.getOwner() instanceof Player)) {
				continue;
			}

			if (e instanceof net.minecraft.world.entity.projectile.hurtingprojectile.windcharge.AbstractWindCharge) {
				kind = "wind_charge";
			} else if (e instanceof net.minecraft.world.entity.projectile.arrow.ThrownTrident trident) {
				if (!trident.entityTags().contains(HIT_TAG) && trident.getDeltaMovement().lengthSqr() > 1.0E-4) {
					kind = "trident";
				}
			} else if (e instanceof net.minecraft.world.entity.projectile.throwableitemprojectile.AbstractThrownPotion) {
				kind = "potion";
			} else if (e instanceof AbstractArrow arrow && !arrow.entityTags().contains(HIT_TAG) && arrow.getDeltaMovement().lengthSqr() > 1.0E-4) {
				kind = "arrow";
			} else if (e instanceof FireworkRocketEntity rocket && rocket.isShotAtAngle()) {
				kind = "firework"; // not the ones boosting an elytra flight
			} else if (e instanceof Fireball) {
				kind = "fireball";
			} else if (!(e instanceof ThrownEnderpearl) && !(e instanceof AbstractArrow) && !(e instanceof FireworkRocketEntity)) {
				kind = "throwable";
			}

			if (kind != null) {
				b = b == null ? new StringBuilder("{\"t\":\"proj\",\"p\":[") : b.append(',');
				b.append(String.format(Locale.ROOT, "[%d,\"%s\",%.3f,%.3f,%.3f]", e.getId(), kind, e.getX(), e.getY(), e.getZ()));
			}
		}

		if (b != null) {
			Passthrough.events.accept(b.append("]}").toString());
		}
	}

	/**
	 * The host traced a projectile into something of its own: a firework bursts there; an arrow goes into a person
	 * or car (gone) or sticks where it hit a wall. Never discard tridents (Loyalty return), wind charges, potions, or fireballs!
	 */
	public static void projectileHit(final int id, final double x, final double y, final double z, final boolean stick) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			Entity e = level.getEntity(id);
			if (e instanceof net.minecraft.world.entity.projectile.arrow.ThrownTrident
				|| e instanceof net.minecraft.world.entity.projectile.hurtingprojectile.windcharge.AbstractWindCharge
				|| e instanceof net.minecraft.world.entity.projectile.throwableitemprojectile.AbstractThrownPotion
				|| e instanceof Fireball) {
				return;
			}
			if (e instanceof FireworkRocketEntity) {
				e.setPos(x, y, z);
				level.broadcastEntityEvent(e, (byte) 17);
				e.discard();
			} else if (e instanceof AbstractArrow) {
				if (stick) {
					e.setPos(x, y, z);
					e.setDeltaMovement(Vec3.ZERO);
					e.setNoGravity(true);
					e.addTag(HIT_TAG);
				} else {
					e.discard();
				}
			} else if (e != null) {
				e.discard();
			}
		});
	}

	/** Server thread, from Level.setBlock: remember the change; flushed once per tick. */
	public static void onBlockChanged(final ServerLevel level, final BlockPos pos, final BlockState state) {
		if (!Passthrough.active || level != level.getServer().overworld()) {
			return;
		}

		Nether.onBlockChanged(pos, state);
		if (!placingGround) {
			changes.put(pos.immutable(), solidForHost(level, pos, state));
		}
	}

	/** While on, block changes are the host's own ground being edited (not reported as blocks to collide with). */
	static void quietGround(final boolean on) {
		placingGround = on;
	}

	/** End of each server tick: {"t":"blocks","set":[x,y,z,...],"clear":[x,y,z,...]}. */
	static void flush(final MinecraftServer s) {
		if (changes.isEmpty()) {
			return;
		}

		StringBuilder set = new StringBuilder();
		StringBuilder clear = new StringBuilder();
		for (Map.Entry<BlockPos, Boolean> e : changes.entrySet()) {
			StringBuilder b = e.getValue() ? set : clear;
			BlockPos p = e.getKey();
			b.append(b.isEmpty() ? "" : ",").append(p.getX()).append(',').append(p.getY()).append(',').append(p.getZ());
		}

		changes.clear();
		Passthrough.events.accept("{\"t\":\"blocks\",\"set\":[" + set + "],\"clear\":[" + clear + "]}");
	}

	/** Every solid block within `radius` of the player, as one "blocks" message (the host's props start from this). */
	public static void sync(final int radius) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			ServerLevel level = s.overworld();
			ServerPlayer player = s.getPlayerList().getPlayers().isEmpty() ? null : s.getPlayerList().getPlayers().get(0);
			if (player == null) {
				return;
			}

			BlockPos c = player.blockPosition();
			BlockPos.MutableBlockPos p = new BlockPos.MutableBlockPos();
			int found = 0;
			for (int x = -radius; x <= radius && found < 3000; x++) {
				for (int z = -radius; z <= radius && found < 3000; z++) {
					for (int y = -24; y <= 40; y++) {
						p.set(c.getX() + x, c.getY() + y, c.getZ() + z);
						if (!level.isInWorldBounds(p) || !level.isLoaded(p)) {
							continue;
						}

						BlockState state = level.getBlockState(p);
						if (solidForHost(level, p, state)) {
							changes.put(p.immutable(), true);
							found++;
						}
					}
				}
			}

			flush(s);
		});
	}

	/** Elytra on and gliding (creative flight off), launched forward along the look; or back to creative flight. */
	public static void glide(final boolean start, final double speed) {
		MinecraftServer s = server;
		if (s == null) {
			return;
		}

		s.execute(() -> {
			if (s.getPlayerList().getPlayers().isEmpty()) {
				return;
			}

			ServerPlayer player = s.getPlayerList().getPlayers().get(0);
			if (start) {
				player.setItemSlot(EquipmentSlot.CHEST, new ItemStack(Items.ELYTRA));
				player.getAbilities().flying = false;
				player.onUpdateAbilities();
				player.setOnGround(false);
				player.startFallFlying();
				player.setDeltaMovement(player.getLookAngle().scale(speed));
				player.needsSync = true;
			} else {
				player.stopFallFlying();
				player.setItemSlot(EquipmentSlot.CHEST, ItemStack.EMPTY);
				player.getAbilities().flying = true;
				player.onUpdateAbilities();
			}
		});
	}

	/** `source`: what exploded or was blown up, e.g. "tnt", "creeper", "fireball" (a ghast's). */
	public static void onExplosion(final Vec3 center, final float radius, final String source) {
		if (Passthrough.active) {
			Passthrough.events.accept(String.format(Locale.ROOT, "{\"t\":\"explosion\",\"pos\":[%.3f,%.3f,%.3f],\"r\":%.2f,\"src\":\"%s\"}",
				center.x, center.y, center.z, radius, source));
		}
	}
}
