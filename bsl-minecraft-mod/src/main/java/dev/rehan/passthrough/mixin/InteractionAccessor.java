package dev.rehan.passthrough.mixin;

import net.minecraft.world.entity.Interaction;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Invoker;

@Mixin(Interaction.class)
public interface InteractionAccessor {
	@Invoker("setWidth")
	void passthrough$setWidth(float width);

	@Invoker("setHeight")
	void passthrough$setHeight(float height);

	@Invoker("setResponse")
	void passthrough$setResponse(boolean response);
}
