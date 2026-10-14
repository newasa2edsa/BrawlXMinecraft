package dev.rehan.passthrough.mixin;

import dev.rehan.passthrough.MobWar;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.Interaction;
import net.minecraft.world.entity.player.Player;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(Interaction.class)
abstract class InteractionMixin {
	@Inject(method = "skipAttackInteraction", at = @At("HEAD"), cancellable = true)
	private void passthrough$onMeleeAttack(final Entity entity, final CallbackInfoReturnable<Boolean> cir) {
		Interaction self = (Interaction) (Object) this;
		if (MobWar.isProxy(self) && entity instanceof Player player) {
			MobWar.onProxyHit(self, player.damageSources().playerAttack(player), 7.0F);
			cir.setReturnValue(false);
		}
	}

	@Inject(method = "hurtServer", at = @At("HEAD"), cancellable = true)
	private void passthrough$onProjectileOrExplosionHurt(final ServerLevel level, final DamageSource source, final float amount, final CallbackInfoReturnable<Boolean> cir) {
		Interaction self = (Interaction) (Object) this;
		if (MobWar.isProxy(self)) {
			MobWar.onProxyHit(self, source, amount);
			cir.setReturnValue(true);
		}
	}
}
