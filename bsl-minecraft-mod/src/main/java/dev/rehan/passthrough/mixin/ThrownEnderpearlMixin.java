package dev.rehan.passthrough.mixin;

import dev.rehan.passthrough.MobWar;
import dev.rehan.passthrough.Passthrough;
import dev.rehan.passthrough.WorldBridge;
import java.util.Locale;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.Interaction;
import net.minecraft.world.entity.Relative;
import net.minecraft.world.entity.projectile.throwableitemprojectile.ThrownEnderpearl;
import net.minecraft.world.phys.EntityHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ThrownEnderpearl.class)
abstract class ThrownEnderpearlMixin {
	@Inject(method = "onHitEntity", at = @At("HEAD"), cancellable = true)
	private void passthrough$ignoreProxyEntities(final EntityHitResult hitResult, final CallbackInfo ci) {
		if (hitResult.getEntity() instanceof Interaction || MobWar.isProxy(hitResult.getEntity())) {
			ci.cancel();
		}
	}

	@Inject(method = "onHit", at = @At("HEAD"), cancellable = true)
	private void passthrough$handleAccurateLanding(final HitResult hitResult, final CallbackInfo ci) {
		ThrownEnderpearl self = (ThrownEnderpearl) (Object) this;
		if (!(self.level() instanceof ServerLevel serverLevel)) {
			return;
		}
		if (!(self.getOwner() instanceof ServerPlayer player)) {
			return;
		}

		Vec3 hitPos = hitResult.getLocation();
		double targetX = hitPos.x;
		double targetY = 64.0; // Clamp flush to surface of barrier floor (y=60..63)
		double targetZ = hitPos.z;

		WorldBridge.onPlayerTeleported(targetX, targetY, targetZ);
		Passthrough.events.accept(String.format(Locale.ROOT, "{\"t\":\"pteleport\",\"pos\":[%.3f,%.3f,%.3f]}", targetX, targetY, targetZ));

		// Preserve player's look angles without camera snap or jitter using Relative.ROTATION
		player.teleportTo(serverLevel, targetX, targetY, targetZ, Relative.ROTATION, 0.0F, 0.0F, false);
		player.resetFallDistance();
		self.discard();
		ci.cancel();
	}

	@Inject(method = "tick", at = @At("TAIL"))
	private void passthrough$failsafeFloorLanding(final CallbackInfo ci) {
		ThrownEnderpearl self = (ThrownEnderpearl) (Object) this;
		if (self.isRemoved() || !(self.level() instanceof ServerLevel serverLevel) || !(self.getOwner() instanceof ServerPlayer player)) {
			return;
		}
		if (self.getY() <= 64.0) {
			double targetX = self.getX();
			double targetY = 64.0;
			double targetZ = self.getZ();

			WorldBridge.onPlayerTeleported(targetX, targetY, targetZ);
			Passthrough.events.accept(String.format(Locale.ROOT, "{\"t\":\"pteleport\",\"pos\":[%.3f,%.3f,%.3f]}", targetX, targetY, targetZ));

			player.teleportTo(serverLevel, targetX, targetY, targetZ, Relative.ROTATION, 0.0F, 0.0F, false);
			player.resetFallDistance();
			self.discard();
		}
	}
}
