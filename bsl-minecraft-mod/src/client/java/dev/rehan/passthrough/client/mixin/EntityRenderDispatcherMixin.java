package dev.rehan.passthrough.client.mixin;

import net.minecraft.client.renderer.culling.Frustum;
import net.minecraft.client.renderer.entity.EntityRenderDispatcher;
import net.minecraft.network.chat.Component;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.Interaction;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(EntityRenderDispatcher.class)
abstract class EntityRenderDispatcherMixin {
	@Inject(method = "shouldRender", at = @At("HEAD"), cancellable = true)
	private <E extends Entity> void passthrough$hideProxyEntities(
			final E entity, final Frustum frustum, final double x, final double y, final double z, final float partialTick,
			final CallbackInfoReturnable<Boolean> cir) {
		if (entity instanceof Interaction) {
			cir.setReturnValue(false);
			return;
		}
		Component customName = entity.getCustomName();
		if (customName != null && "__passthrough_proxy__".equals(customName.getString())) {
			cir.setReturnValue(false);
		}
	}
}
