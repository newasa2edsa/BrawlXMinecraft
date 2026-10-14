package dev.rehan.passthrough.client.mixin;

import com.mojang.blaze3d.platform.FramerateLimitTracker;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Never throttle Minecraft to 10/30 FPS when sitting unfocused or hidden behind the host game window. */
@Mixin(FramerateLimitTracker.class)
abstract class FramerateLimitTrackerMixin {
	@Inject(method = "getFramerateLimit", at = @At("HEAD"), cancellable = true)
	private void passthrough$alwaysFullFps(final CallbackInfoReturnable<Integer> cir) {
		cir.setReturnValue(120);
	}

	@Inject(method = "isHeavilyThrottled", at = @At("HEAD"), cancellable = true)
	private void passthrough$neverThrottled(final CallbackInfoReturnable<Boolean> cir) {
		cir.setReturnValue(false);
	}
}
