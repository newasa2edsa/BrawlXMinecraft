package dev.rehan.passthrough.mixin;

import dev.rehan.passthrough.Passthrough;
import dev.rehan.passthrough.WorldBridge;
import java.util.Locale;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.level.portal.TeleportTransition;
import net.minecraft.world.phys.Vec3;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Hooks Entity.teleport(TeleportTransition) for Ender Pearls and portal transitions on modern Minecraft 26.3. */
@Mixin(Entity.class)
abstract class EntityMixin {
	@Inject(method = "teleport(Lnet/minecraft/world/level/portal/TeleportTransition;)Lnet/minecraft/world/entity/Entity;", at = @At("HEAD"))
	private void passthrough$entityTeleported(final TeleportTransition transition, final CallbackInfoReturnable<Entity> cir) {
		if (Passthrough.active && (Object) this instanceof ServerPlayer) {
			Vec3 p = transition.position();
			if (Math.abs(p.x) <= 30.0 && Math.abs(p.z) <= 36.0 && p.y >= 58.0 && p.y <= 90.0) {
				WorldBridge.onPlayerTeleported(p.x, p.y, p.z);
				Passthrough.events.accept(String.format(Locale.ROOT, "{\"t\":\"pteleport\",\"pos\":[%.3f,%.3f,%.3f]}", p.x, p.y, p.z));
			}
		}
	}
}
