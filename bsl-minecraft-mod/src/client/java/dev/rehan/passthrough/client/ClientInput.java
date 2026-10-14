package dev.rehan.passthrough.client;

import com.google.gson.JsonObject;
import com.mojang.blaze3d.platform.Window;
import dev.rehan.passthrough.Passthrough;
import dev.rehan.passthrough.WorldBridge;
import dev.rehan.passthrough.client.mixin.KeyMappingAccessor;
import net.minecraft.client.KeyMapping;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.inventory.InventoryScreen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.MouseButtonInfo;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.entity.player.Inventory;
import org.lwjgl.sdl.SDLVideo;

/** Host input, applied on the client thread: the host window has the focus, so Minecraft never sees these itself. */
final class ClientInput {
	private static double lastScaledX = -1.0;
	private static double lastScaledY = -1.0;
	private static int heldBtn = -1;

	private ClientInput() {
	}

	private static void moveCursor(final Minecraft minecraft, final Window win, final double rawX, final double rawY) {
		double px = (rawX >= 0.0 && rawX <= 1.0) ? rawX * win.getScreenWidth() : Math.clamp(rawX, 0.0, (double)win.getScreenWidth());
		double py = (rawY >= 0.0 && rawY <= 1.0) ? rawY * win.getScreenHeight() : Math.clamp(rawY, 0.0, (double)win.getScreenHeight());
		minecraft.mouseHandler.onMove(win.handle(), px, py, 0.0, 0.0);
		if (minecraft.gui.screen() != null) {
			double sx = minecraft.mouseHandler.getScaledXPos(win);
			double sy = minecraft.mouseHandler.getScaledYPos(win);
			minecraft.gui.screen().mouseMoved(sx, sy);
			if (heldBtn >= 0 && lastScaledX >= 0.0 && (sx != lastScaledX || sy != lastScaledY)) {
				minecraft.gui.screen().mouseDragged(
					new MouseButtonEvent(sx, sy, new MouseButtonInfo(heldBtn, 0)),
					sx - lastScaledX,
					sy - lastScaledY
				);
			}
			lastScaledX = sx;
			lastScaledY = sy;
		}
	private static void openInventoryScreen(final Minecraft minecraft, final LocalPlayer player) {
		if (player == null) return;
		if (minecraft.gameMode != null && minecraft.gameMode.hasInfiniteItems()) {
			minecraft.gui.setScreen(new net.minecraft.client.gui.screens.inventory.CreativeModeInventoryScreen(
				player,
				player.connection.enabledFeatures(),
				minecraft.options.operatorItemsTab().get()
			));
		} else {
			minecraft.gui.setScreen(new InventoryScreen(player));
		}
	}

	static void handle(final Minecraft minecraft, final JsonObject m) {
		LocalPlayer player = minecraft.player;
		switch (m.get("t").getAsString()) {
			case "inv" -> {
				boolean wantOpen = !m.has("open") || m.get("open").getAsBoolean();
				if (!wantOpen || minecraft.gui.screen() != null) {
					if (minecraft.gui.screen() != null) {
						minecraft.gui.screen().onClose();
						minecraft.gui.setScreen(null);
					}
					heldBtn = -1;
				} else if (player != null) {
					openInventoryScreen(minecraft, player);
				}
			}
			case "mouse" -> {
				Window win = minecraft.getWindow();
				moveCursor(minecraft, win, m.get("x").getAsDouble(), m.get("y").getAsDouble());
			}
			case "mbtn" -> {
				Window win = minecraft.getWindow();
				if (m.has("x") && m.has("y")) {
					moveCursor(minecraft, win, m.get("x").getAsDouble(), m.get("y").getAsDouble());
				}
				int btn = m.has("b") ? m.get("b").getAsInt() : 0;
				int mods = m.has("mod") ? m.get("mod").getAsInt() : 0;
				boolean down = !m.has("down") || m.get("down").getAsBoolean();
				heldBtn = down ? btn : -1;
				minecraft.mouseHandler.onButton(win.handle(), new MouseButtonInfo(btn, mods), down ? 1 : 0);
			}
			case "mscroll" -> {
				Window win = minecraft.getWindow();
				if (m.has("x") && m.has("y")) {
					moveCursor(minecraft, win, m.get("x").getAsDouble(), m.get("y").getAsDouble());
				}
				double d = m.get("d").getAsDouble();
				minecraft.mouseHandler.onScroll(win.handle(), 0.0, d);
			}
			case "key" -> {
				String k = m.get("k").getAsString();
				boolean down = !m.has("down") || m.get("down").getAsBoolean();
				if (k.equals("escape")) {
					if (down && minecraft.gui.screen() != null) {
						minecraft.gui.screen().onClose();
						minecraft.gui.setScreen(null);
					}
					heldBtn = -1;
					return;
				}
				if (k.equals("inventory")) {
					if (down) {
						if (minecraft.gui.screen() != null) {
							minecraft.gui.screen().onClose();
							minecraft.gui.setScreen(null);
							heldBtn = -1;
						} else if (player != null) {
							openInventoryScreen(minecraft, player);
						}
					}
					return;
				}
				if (k.equals("options")) {
					if (down) {
						if (minecraft.gui.screen() != null) {
							minecraft.gui.screen().onClose();
							minecraft.gui.setScreen(null);
							heldBtn = -1;
						} else {
							minecraft.gui.setScreen(new net.minecraft.client.gui.screens.options.OptionsScreen(null, minecraft.options));
						}
					}
					return;
				}
				if (k.equals("chat")) {
					if (down) {
						boolean isCmd = m.has("cmd") && m.get("cmd").getAsBoolean();
						minecraft.gui.setScreen(new net.minecraft.client.gui.screens.ChatScreen(isCmd ? "/" : "", false));
					}
					return;
				}

				KeyMapping key = switch (k) {
					case "use" -> minecraft.options.keyUse;
					case "attack" -> minecraft.options.keyAttack;
					case "pick" -> minecraft.options.keyPickItem;
					case "drop" -> minecraft.options.keyDrop;
					case "swap" -> minecraft.options.keySwapOffhand;
					case "jump" -> minecraft.options.keyJump;
					case "sneak" -> minecraft.options.keyShift;
					case "sprint" -> minecraft.options.keySprint;
					default -> null;
				};
				if (k.equals("attack") && down && player != null) {
					String itemPath = BuiltInRegistries.ITEM.getKey(player.getMainHandItem().getItem()).getPath();
					WorldBridge.onPlayerAttack();
					Passthrough.events.accept("{\"t\":\"melee\",\"item\":\"" + itemPath + "\"}");
				}
				if (k.equals("use") && down && player != null) {
					String itemPath = BuiltInRegistries.ITEM.getKey(player.getMainHandItem().getItem()).getPath();
					if (itemPath.equals("fire_charge")) {
						WorldBridge.shootFireball();
					} else {
						WorldBridge.onPlayerUseItem(itemPath);
					}
					Passthrough.events.accept("{\"t\":\"use_item\",\"item\":\"" + itemPath + "\"}");
				}

				if (key != null) {
					if (down && !key.isDown()) {
						KeyMappingAccessor access = (KeyMappingAccessor)key;
						access.passthrough$setClickCount(access.passthrough$getClickCount() + 1);
					}

					key.setDown(down);
				}
			}
			case "slot" -> {
				int slot = Math.clamp(m.get("n").getAsInt(), 0, Inventory.getSelectionSize() - 1);
				if (minecraft.gui.screen() != null) {
					// Pressing 1-9 while hovering an item in the Inventory assigns it to hotbar slot 1-9!
					minecraft.gui.screen().keyPressed(new KeyEvent(49 + slot, 2 + slot, 0));
				} else if (player != null) {
					player.getInventory().setSelectedSlot(slot);
				}
			}
			case "scroll" -> {
				if (player != null) {
					Inventory inventory = player.getInventory();
					int size = Inventory.getSelectionSize();
					inventory.setSelectedSlot(Math.floorMod(inventory.getSelectedSlot() - m.get("d").getAsInt(), size));
				}
			}
			case "hud" -> {
				if (minecraft.gui.hud.isHidden() != m.get("hidden").getAsBoolean()) {
					minecraft.gui.hud.toggle();
				}
			}
			case "view" -> {
				// match the host's picture exactly: un-minimize/un-maximize first (resizing a maximized window is ignored)
				int w = m.get("w").getAsInt(), h = m.get("h").getAsInt();
				long handle = minecraft.getWindow().handle();
				SDLVideo.SDL_RestoreWindow(handle);
				minecraft.getWindow().setWindowed(w, h);
				SDLVideo.SDL_SetWindowSize(handle, w, h);
				SDLVideo.SDL_SyncWindow(handle);
			}
			case "chat" -> {
				boolean isCmd = m.has("cmd") && m.get("cmd").getAsBoolean();
				minecraft.gui.setScreen(new net.minecraft.client.gui.screens.ChatScreen(isCmd ? "/" : "", false));
			}
			case "type" -> {
				if (minecraft.gui.screen() != null) {
					if (m.has("char")) {
						String chStr = m.get("char").getAsString();
						for (int i = 0; i < chStr.length(); ) {
							int cp = chStr.codePointAt(i);
							minecraft.gui.screen().charTyped(new net.minecraft.client.input.CharacterEvent(cp));
							i += Character.charCount(cp);
						}
					}
					if (m.has("key")) {
						int code = m.get("key").getAsInt();
						int scan = m.has("scan") ? m.get("scan").getAsInt() : 0;
						int mod = m.has("mod") ? m.get("mod").getAsInt() : 0;
						minecraft.gui.screen().keyPressed(new KeyEvent(code, scan, mod));
					}
				}
			}
			default -> {
			}
		}
	}
}
