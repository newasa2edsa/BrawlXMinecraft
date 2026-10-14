package dev.universalmodder.bslbridge;

import net.minecraft.core.BlockPos;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.phys.Vec3;

/**
 * Maps Brawl Stars v68 logic coordinates into Minecraft world coordinates.
 *
 * <p>Brawl Stars logic scale:
 * <ul>
 *   <li>1 tile = 300 logic units = 1.0 Minecraft block</li>
 *   <li>Standard 3v3 map = 21 x 33 tiles (6300 x 9900 units), center = (3150, 4950)</li>
 * </ul>
 */
public final class BslCoordinateMapper {
	public static final double UNITS_PER_BLOCK = 300.0;
	public static final int DEFAULT_ARENA_CENTER_X = 3150;
	public static final int DEFAULT_ARENA_CENTER_Y = 4950;

	private static volatile Vec3 anchor = new Vec3(0.0, 64.0, 0.0);
	private static volatile boolean anchoredToPlayer = false;

	private BslCoordinateMapper() {
	}

	public static Vec3 getAnchor() {
		return anchor;
	}

	public static void setAnchor(final Vec3 pos) {
		anchor = pos;
		anchoredToPlayer = true;
	}

	public static void resetAnchor() {
		anchoredToPlayer = false;
	}

	/**
	 * Anchors the BSL arena center (3150, 4950) 4 blocks in front of the first online player
	 * if the user has not manually set an anchor via /bsl anchor.
	 */
	public static void ensureAnchoredToPlayer(final MinecraftServer server) {
		if (anchoredToPlayer || server.getPlayerList().getPlayers().isEmpty()) {
			return;
		}
		ServerPlayer player = server.getPlayerList().getPlayers().get(0);
		Vec3 forward = Vec3.directionFromRotation(0.0F, player.getYRot()).scale(4.0);
		anchor = new Vec3(
			Math.floor(player.getX() + forward.x) + 0.5,
			Math.floor(player.getY()),
			Math.floor(player.getZ() + forward.z) + 0.5
		);
		anchoredToPlayer = true;
	}

	/**
	 * Converts BSL (x, y, z) in logic units to Minecraft (x, y, z) in blocks,
	 * snapping to the nearest walkable surface height if z == 0.
	 */
	public static Vec3 toMinecraft(final ServerLevel level, final int bslX, final int bslY, final int bslZ) {
		Vec3 a = anchor;
		double mcX = a.x + (bslX - DEFAULT_ARENA_CENTER_X) / UNITS_PER_BLOCK;
		double mcZ = a.z + (bslY - DEFAULT_ARENA_CENTER_Y) / UNITS_PER_BLOCK;
		double mcY = a.y + (bslZ / UNITS_PER_BLOCK);

		if (bslZ == 0 && level != null) {
			BlockPos surface = findSurface(level, (int) Math.floor(mcX), (int) Math.floor(a.y), (int) Math.floor(mcZ));
			if (surface != null) {
				mcY = surface.getY();
			}
		}

		return new Vec3(mcX, mcY, mcZ);
	}

	public static float toMinecraftYaw(final double dxUnits, final double dyUnits, final float fallbackYaw) {
		if (Math.abs(dxUnits) < 1.0E-3 && Math.abs(dyUnits) < 1.0E-3) {
			return fallbackYaw;
		}
		return (float) (Math.atan2(-dxUnits, dyUnits) * (180.0 / Math.PI));
	}

	private static BlockPos findSurface(final ServerLevel level, final int x, final int baseY, final int z) {
		BlockPos.MutableBlockPos pos = new BlockPos.MutableBlockPos();
		for (int dy = 5; dy >= -8; dy--) {
			pos.set(x, baseY + dy, z);
			if (!level.isInWorldBounds(pos) || !level.isLoaded(pos)) {
				continue;
			}
			if (!level.getBlockState(pos).isAir()
				&& level.getBlockState(pos.above()).isAir()
				&& level.getBlockState(pos.above(2)).isAir()) {
				return pos.above().immutable();
			}
		}
		return null;
	}
}
