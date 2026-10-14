package dev.rehan.passthrough.mixin;

import dev.rehan.passthrough.Passthrough;
import dev.rehan.passthrough.WorldBridge;
import java.util.Locale;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.level.portal.TeleportTransition;
import net.minecraft.world.phys.Vec3;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Minecraft moving the player (ender pearls / teleportTo) moves the host's player too; otherwise the host would pull it back. */
@Mixin(ServerPlayer.class)
abstract class ServerPlayerMixin {
	@Inject(method = "teleport(Lnet/minecraft/world/level/portal/TeleportTransition;)Lnet/minecraft/server/level/ServerPlayer;", at = @At("HEAD"))
	private void passthrough$teleportTransition(final TeleportTransition transition, final CallbackInfoReturnable<ServerPlayer> cir) {
		if (Passthrough.active) {
			Vec3 p = transition.position();
			if (Math.abs(p.x) <= 30.0 && Math.abs(p.z) <= 36.0 && p.y >= 58.0 && p.y <= 90.0) {
				WorldBridge.onPlayerTeleported(p.x, p.y, p.z);
				Passthrough.events.accept(String.format(Locale.ROOT, "{\"t\":\"pteleport\",\"pos\":[%.3f,%.3f,%.3f]}", p.x, p.y, p.z));
			}
		}
	}

	@Inject(method = "teleportTo(Lnet/minecraft/server/level/ServerLevel;DDDLjava/util/Set;FFZ)Z", at = @At("HEAD"))
	private void passthrough$teleportTo(final ServerLevel level, final double x, final double y, final double z,
			final java.util.Set<?> rel, final float yaw, final float pitch, final boolean resetCam,
			final CallbackInfoReturnable<Boolean> cir) {
		if (Passthrough.active) {
			if (Math.abs(x) <= 30.0 && Math.abs(z) <= 36.0 && y >= 58.0 && y <= 90.0) {
				WorldBridge.onPlayerTeleported(x, y, z);
				Passthrough.events.accept(String.format(Locale.ROOT, "{\"t\":\"pteleport\",\"pos\":[%.3f,%.3f,%.3f]}", x, y, z));
			}
		}
	}
}
