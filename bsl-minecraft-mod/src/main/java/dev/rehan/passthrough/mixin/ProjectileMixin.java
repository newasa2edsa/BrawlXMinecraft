package dev.rehan.passthrough.mixin;

import dev.rehan.passthrough.MobWar;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.Interaction;
import net.minecraft.world.entity.projectile.Projectile;
import net.minecraft.world.entity.projectile.throwableitemprojectile.ThrownEnderpearl;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** All Steve projectiles (arrows, fireworks, tridents, fireballs, wind charges, potions, snowballs) hit enemy proxies; Ender Pearls pass through proxies so they land on the arena floor. */
@Mixin(Projectile.class)
abstract class ProjectileMixin {
	@Shadow
	public abstract Entity getOwner();

	@Inject(method = "canHitEntity(Lnet/minecraft/world/entity/Entity;)Z", at = @At("HEAD"), cancellable = true)
	private void passthrough$throughProxies(final Entity target, final CallbackInfoReturnable<Boolean> cir) {
		if (target instanceof Interaction || MobWar.isProxy(target)) {
			Object self = this;
			cir.setReturnValue(!(self instanceof ThrownEnderpearl));
		}
	}
}
