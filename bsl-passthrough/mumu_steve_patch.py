"""Patch the installed BSL v68 client inside MuMu Player via ADB for True First-Person POV:
1. Patches `lib/arm64/libg.so` (backing up `libg.so.orig` first) to:
   - Enable 3D rotatable camera (`m_cameraMode = 3`) in `BattleScreen::init`
   - Convert `BattleScreen::updateCamera` into True First-Person POV at eye height `z = 500.0f` (`1.667` blocks)
     with level horizon (`pitch = 0`), free-look without snap-back, and `zNear = 50.0f`
   - Hide Brawl Stars 2D battle UI (`CombatGUI`, `CombatHUD`, floating health bars/names) while keeping
     all touch / WASD joystick controls 100% functional
   - Hide the local player's own 3D brawler mesh & shadow (`Character3D::update` when `m_isOwnCharacter == 1`)
     so you never see Shelly's body, while keeping all enemy 3D brawlers visible
2. Installs `skills.csv`, `characters.csv`, and `texts.csv` under `/data/data/com.bsl.v68/update/` to:
   - Rename Shelly -> "Steve" in UI
   - Disable Shelly's shotgun pellets and muzzle flash (`ShotgunGirlWeapon` / `ShotgunGirlUlti`) without crashing

Usage:
    python bsl-passthrough/mumu_steve_patch.py          # Apply First-Person POV & Steve patch
    python bsl-passthrough/mumu_steve_patch.py --remove # Restore original libg.so and remove update overrides
"""
from __future__ import annotations

import csv
import io
import os
import subprocess
import sys
import tempfile
import time
import zipfile

ADB_CANDIDATES = [
    r"C:\Program Files\Netease\MuMuPlayer\nx_device\12.0\shell\adb.exe",
    r"C:\Program Files\Netease\MuMuPlayer\nx_main\adb.exe",
    r"C:\Program Files\Netease\MuMuPlayer\nx_device\15.0\shell\adb.exe",
]
MUMU_PORTS = ["127.0.0.1:16448", "127.0.0.1:7555", "127.0.0.1:16384", "127.0.0.1:16416", "127.0.0.1:5559"]
APK_PATH = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "com.bsl.v68-rev2.apk"))
PKG = "com.bsl.v68"
PATCH_STAMP = "fp_steve_v19"

# Verified ARM64 patches in lib/arm64-v8a/libg.so (PT_LOAD 0: file_offset == vaddr)
LIBG_ARM64_PATCHES: list[tuple[int, str, str]] = [
    # 0. PushNotificationService::update: skip push notification account-ID check (0x824978..0x8249c4)
    #    while keeping MessageManager::loginOkReceived (0x816170) 100% original!
    (0x82495C, "e8000034", "1f2003d5"),  # nop (fall through to 0x824960 epilogue)
    # 1. BattleScreen::init: clear stale own_cli_ptr/obj_mgr_srv/explosion .bss at 0x1310778..0x131078f
    #    (strictly inside PHDR 3 < 0x13107e8), skip 0x9be5e8() NULL return dereference, and force m_cameraMode = 3
    (0x8595F4, "fd930594", "a85500f0"),  # adrp x8, #0x1310000
    (0x8595F8, "081440f9", "1fc103f9"),  # str xzr, [x8, #0x780] (clear obj_mgr_srv before match starts)
    (0x8595FC, "01ccffd0", "1fc503f9"),  # str xzr, [x8, #0x788] (clear pending_exp_x & pending_exp_y)
    (0x859600, "21842191", "1fbd03f9"),  # str xzr, [x8, #0x778] (clear own_cli_ptr before match starts)
    (0x859604, "089540f9", "04000014"),  # b #0x859614 (jump straight to mov w8, #3; str w8, [x19, #0x90c])
    (0x859610, "60000036", "1f2003d5"),  # nop
    (0x859614, "28008052", "68008052"),  # mov w8, #3
    (0x859618, "681e09b9", "680e09b9"),  # str w8, [x19, #0x90c]
    # 2. BattleScreen::updateCamera: True First-Person eye & target + locked 60.0f FOV + export x22 (LogicCharacterClientOwn*)
    (0x85a680, "0008011f", "00e4002f"),  # movi d0, #0 (init m_cameraPitch = 0.0f level horizon)
    (0x85a8e0, "0109211e", "084ea852"),  # mov w8, #0x42700000 (lock vertical FOV to 60.0f to match Minecraft 1:1)
    (0x85a8e4, "2908031f", "0901271e"),  # fmov s9, w8 (s9 = 60.0f constant vertical FOV)
    (0x85a928, "68226639", "a85500d0"),  # adrp x8, #0x1310000
    (0x85a92c, "28050034", "16bd03f9"),  # str x22, [x8, #0x778] (export genuine LogicCharacterClientOwn* to 0x1310778!)
    (0x85a930, "01103e1e", "28000014"),  # b #0x85a9d0 (lock eye directly to 60FPS interpolated player X/Y, zero spring oscillation)
    (0x85aa00, "763c00b4", "e3010014"),  # b #0x85b18c (never run nearest-enemy camera auto-lock)
    (0x85b194, "0c30231e", "0ce4002f"),  # movi d12, #0 (eliminate 6.25x pan offset so eye stays strictly on player)
    (0x85b1c8, "68266639", "e8031f2a"),  # mov w8, wzr (allow free-look while moving & prevent snap-back)
    (0x85b1d4, "560500b4", "55000014"),  # b #0x85b328 (bypass touch-drag & auto-recenter so free mouse-look controls +0x958/+0x95c 100% cleanly!)
    (0x85b438, "424c211e", "2840201e"),  # fmov s8, s1 (bypass map minPitch/maxPitch = 0 clamp)
    (0x85b448, "684c221e", "1f2003d5"),  # nop (keep s8 = m_cameraPitch)
    (0x85b458, "01102c1e", "01e4002f"),  # movi d1, #0 (remove +0.5 downward third-person pitch bias)
    (0x85b460, "6bd608bd", "6bca08bd"),  # str s11, [x19, #0x8c8] (eye.x = playerX)
    (0x85b46c, "68de08b9", "68d208b9"),  # str w8, [x19, #0x8d0] (eye.z = 500.0f)
    (0x85b47c, "422c0e1f", "42ac0e1f"),  # fmsub s2, s2, s14, s11 (target.x = playerX - sin(yaw)*dist -> W=Forward, S=Back, A=Left, D=Right!)
    (0x85b484, "6dda08bd", "6dce08bd"),  # str s13, [x19, #0x8cc] (eye.y = -playerY)
    (0x85b48c, "01282d1e", "a139201e"),  # fsub s1, s13, s0 (target.y = -playerY + cos(yaw)*dist)
    (0x85b490, "63100e1f", "63900e1f"),  # fmsub s3, s3, s14, s4 (target.z = 500.0f - pitch*dist)
    (0x85b494, "62ca08bd", "62d608bd"),  # str s2, [x19, #0x8d4] (store target.x)
    (0x85b498, "61ce08bd", "61da08bd"),  # str s1, [x19, #0x8d8] (store target.y)
    (0x85b49c, "63d208bd", "63de08bd"),  # str s3, [x19, #0x8dc] (store target.z)
    (0x85b4a0, "48010034", "0a000014"),  # b #0x85b4c8 (skip 3rd-person mirror Y negation)
    (0x85b4e0, "487fa852", "0849a852"),  # mov w8, #0x42480000 (zNear = 50.0f so nearby walls don't clip)
    (0x85b514, "606604fd", "1f2003d5"),  # nop (never clamp eye.x/eye.y to isometric map scroll box)
    (0x85b51c, "810200fd", "1f2003d5"),  # nop (never shift target.x/target.y by scroll clamp delta)
    # 3. Hide 2D Battle UI (CombatGUI, CombatHUD) and local player's own healthbar/ammo/name/ground-ring safely
    (0x861be8, "09210039", "1f210039"),  # strb wzr, [x8, #8] (m_combatGui->m_visible = 0)
    (0x55bb34, "c02d0036", "6e010014"),  # b #0x55c0ec (skip CombatHUD & 2D battle overlays in Scene3D::render)
    (0x50eb0c, "e80f1dfc", "c0035fd6"),  # ret (skip 2D floating status bar draw)
    (0x52e888, "60040037", "1f2003d5"),  # nop (when m_isOwnCharacter == 1, fall through to 0x52e88c: b #0x52f048)
    (0x52e910, "c0390036", "ce010014"),  # b #0x52f048 (always hide own healthbar +0x2f8 and ground circle +0x3a8)
    (0x52f044, "80c60737", "1f2003d5"),  # nop (fall through to 0x52f048 instead of re-enabling own healthbar)
    (0x52fa44, "69725c39", "e9031f2a"),  # mov w9, wzr (never set m_ammoBarSprite->m_visible = 1)
    # 4. Disable local player's Brawl Stars attack/super/gadget & ignore right-half screen touches cleanly
    #    (keeps skills.csv 100% original so Bot AI never crashes on NULL ProjectileData!)
    (0x85bcb8, "60f644f9", "78ffff17"),  # b #0x85ba98 (return 0 cleanly on right-side touches; never jump into 0x85bd24 with uninit s8/s9/w21!)
    (0x86f4a0, "ff8306d1", "e0031f2a"),  # mov w0, wzr (disable local player BattleScreen::activateSuper)
    (0x86f4a4, "fd7b14a9", "c0035fd6"),  # ret
    (0x87049c, "ffc305d1", "e0031f2a"),  # mov w0, wzr (disable local player BattleScreen::activateAttack)
    (0x8704a0, "fd7b11a9", "c0035fd6"),  # ret
    # 5. Hide local player's own 3D model & shadow in Character3D::update (keep enemies visible, null-guarded for 100% rock-solid lobby loading)
    (0x83de50, "881e4539", "688a40f9"),  # ldr x8, [x19, #0x110] (LogicCharacterClient*)
    (0x83de54, "68000034", "680000b4"),  # cbz x8, #0x83de60 (IF NULL, SKIP! Never crash in lobby / main menu!)
    (0x83de58, "88ea4539", "08715c39"),  # ldrb w8, [x8, #0x71c] (m_isOwnCharacter)
    (0x83de5c, "e8200034", "e8200035"),  # cbnz w8, #0x83e278 (hide own 3D character mesh & shadow)
    # 6. Authoritative Brawler Damage & Environment Destruction Hook in LogicCharacterServer:
    #    Exports obj_mgr_srv to 100% unused .bss 0x1310780 (strictly inside PHDR 3 < 0x13107e8),
    #    calls 0x55bf80 to trigger Brawl Stars wall/bush destruction on Minecraft TNT/End-Crystal/Respawn-Anchor explosions,
    #    and deals true damage from [srvObj+0xf0c].
    (0xB387A8, "1f080fb9", "1f8407f9"),  # str xzr, [x0, #0xf08] (zero-init +0xf08 and +0xf0c in ctor)
    (0xB3E60C, "a5bd0094", "883e00d0"),  # adrp x8, #0x1310000 (100% unused .bss page inside PHDR 3)
    (0xB3E610, "00f442f9", "691240f9"),  # ldr x9, [x19, #0x20] (this->m_pGameObjectManagerServer)
    (0xB3E614, "000100b4", "09c103f9"),  # str x9, [x8, #0x780] (export obj_mgr_srv into 0x1310780)
    (0xB3E618, "080040f9", "5a76e897"),  # bl #0x55bf80 (call steve_explosion_hook for wall/bush destruction)
    (0xB3E61C, "084940f9", "e00313aa"),  # mov x0, x19 (restore x0 = this after bl #0x55bf80)
    (0xB3E620, "00013fd6", "740e4fb9"),  # ldr w20, [x19, #0xf0c] (read pendingDamage)
    (0xB3E624, "f403002a", "94000034"),  # cbz w20, #0xb3e634 (if 0, continue normal tickRegeneration)
    (0xB3E628, "68c65039", "7f0e0fb9"),  # str wzr, [x19, #0xf0c] (consume pendingDamage)
    (0xB3E62C, "a8020035", "83000014"),  # b #0xb3e838 (call LogicCharacterServer::causeDamage)
    (0xB3E858, "e7031f2a", "27008052"),  # mov w7, #1 (allow damage when m_canRegenerate #0x431 == 1)
    (0xB3E880, "ffc30039", "e8c30039"),  # strb w8, [sp, #0x30] (enable floating damage numbers on hit)
    (0xB3E884, "ffa30039", "e8a30039"),  # strb w8, [sp, #0x28] (bypass shields/immunity and deal true damage)
    # 7. Crash-Safe Brawl Stars Wall/Bush Destruction Code Cave at 0x55bf80..0x55bfd4:
    #    Reads (worldX, worldY, radius) from 0x1310788..0x1310790 and calls LogicTileMap::destroyEnvironment (0xa575c0)
    #    using x19 (which is already a guaranteed LogicCharacterServer*), never overwriting x19 with ptrs[0]
    #    and never mutating m_gameObjects while LogicGameObjectManagerServer::tick is iterating!
    (0x55BF80, "08b840b9", "018947b9"),  # ldr w1, [x8, #0x788] (pending_exp_x)
    (0x55BF84, "a22301d1", "81020034"),  # cbz w1, #0x55bfd4 (if 0, return immediately)
    (0x55BF88, "e027803d", "028d47b9"),  # ldr w2, [x8, #0x78c] (pending_exp_y)
    (0x55BF8C, "a0435bbc", "039147b9"),  # ldr w3, [x8, #0x790] (pending_exp_radius)
    (0x55BF90, "bfc31b78", "1f8907b9"),  # str wzr, [x8, #0x788] (consume pending_exp_x = 0)
    (0x55BF94, "1f090071", "090200b4"),  # cbz x9, #0x55bfd4
    (0x55BF98, "bfe31b38", "600a40f9"),  # ldr x0, [x19, #0x10] (LogicTileMap* from LogicGameObjectServer)
    (0x55BF9C, "e306971a", "c00100b4"),  # cbz x0, #0x55bfd4 (if NULL, return)
    (0x55BFA0, "090040f9", "ff8300d1"),  # sub sp, sp, #0x20
    (0x55BFA4, "290940f9", "fd7b01a9"),  # stp x29, x30, [sp, #0x10]
    (0x55BFA8, "20013fd6", "e4031f2a"),  # mov w4, wzr (isNormalWeapon = 0 -> breaks walls & bushes!)
    (0x55BFAC, "e5ffff17", "658240b9"),  # ldr w5, [x19, #0x80] (ownerIndex)
    (0x55BFB0, "0e590c94", "06008012"),  # mov w6, #-1 (bypass team goal-wall protection, destroy walls anywhere)
    (0x55BFB4, "137c4bb9", "e7e18452"),  # mov w7, #0x270f (damage = 9999 instant wall/bush annihilation)
    (0x55BFB8, "7f060071", "ff030039"),  # strb wzr, [sp] (destroysOnlyBushes = 0)
    (0x55BFBC, "0b040054", "e90700f9"),  # str x9, [sp, #8] (LogicGameObjectManagerServer*)
    (0x55BFC0, "c02ec23d", "80ed1394"),  # bl #0xa575c0 (LogicTileMap::destroyEnvironment)
    (0x55BFC4, "f5031f2a", "fd7b41a9"),  # ldp x29, x30, [sp, #0x10]
    (0x55BFC8, "14008012", "ff830091"),  # add sp, sp, #0x20
    (0x55BFCC, "e01b803d", "c0035fd6"),  # ret
    (0x55BFD0, "04000014", "1f2003d5"),  # nop
    (0x55BFD4, "b5060011", "c0035fd6"),  # ret
    # 8. Weapon Initialization Null Guard in LogicCharacterServer::init (fixes tombstone_24 crash!):
    #    When [x19, #0xc0][x26] (x0) is NULL, return immediately instead of dereferencing NULL at 0xb7db28!
    (0xB331D8, "442a0194", "80a3e897"),  # bl #0x55bfd8 (jump to null check code cave)
    (0x55BFD8, "7f02156b", "400000b4"),  # cbz x0, #0x55bfe0 (if x0 == NULL, return immediately!)
    (0x55BFDC, "00030054", "c3861814"),  # b #0xb7dae8 (if x0 != NULL, jump to weapon init!)
    (0x55BFE0, "ffa300f9", "c0035fd6"),  # ret (clean return to 0xb331dc, skipping null slot!)
    # 9. Array Element Null Guard in 0xbabc7c (fixes tombstone_29 crash at 0xbabc80 [x0, #0x54] when entering battle!):
    #    When an array element in GameObject/Camera list is NULL, skip it immediately to 0xbabc6c instead of dereferencing NULL!
    (0xBABC7C, "207978f8", "dac0e617"),  # b #0x55bfe4
    (0xBABC80, "095440b9", "1f2003d5"),  # nop
    (0x55BFE4, "01590c94", "207978f8"),  # ldr x0, [x9, x24, lsl #3]
    (0x55BFE8, "e2030591", "600000b4"),  # cbz x0, #0x55bff4 (if NULL, skip!)
    (0x55BFEC, "e103152a", "095440b9"),  # ldr w9, [x0, #0x54]
    (0x55BFF0, "8d760c94", "253f1914"),  # b #0xbabc84 (continue)
    (0x55BFF4, "e0a340f9", "1e3f1914"),  # b #0xbabc6c (skip to next item)
    # 10. StatusEffectData Null Guard in 0xba5c7c (fixes tombstone_31 crash at 0xba5d30 [x8, #0x98] when entering battle!):
    #     When StatusEffectData is not found in CSV logic (x22 == NULL), 0xba5cd0 branches to 0xba5d68 to log the warning.
    #     After logging at 0xba5d88, call 0xba5bfc to reset the element to safe inactive defaults and return cleanly via 0xba5ee4!
    (0xBA5D8C, "e00314aa", "e00313aa"),  # mov x0, x19
    (0xBA5D90, "413e8052", "9bffff97"),  # bl #0xba5bfc (reset status effect to safe inactive defaults)
    (0xBA5D94, "e64f0394", "54000014"),  # b #0xba5ee4 (clean return through epilogue, never dereference NULL!)
    (0xBA5D98, "a0fa0737", "1f2003d5"),  # nop
]


def find_adb() -> str | None:
    for p in ADB_CANDIDATES:
        if os.path.exists(p):
            return p
    return None


def run_adb(adb: str, args: list[str], serial: str | None = None, timeout: int = 30) -> subprocess.CompletedProcess:
    cmd = [adb]
    if serial:
        cmd.extend(["-s", serial])
    cmd.extend(args)
    return subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)


def connect_mumu(adb: str) -> str | None:
    import socket
    run_adb(adb, ["start-server"], timeout=10)
    for target in MUMU_PORTS:
        host, port_str = target.split(":")
        port = int(port_str)
        try:
            with socket.create_connection((host, port), timeout=0.2):
                run_adb(adb, ["connect", target], timeout=6)
        except OSError:
            continue
    res = run_adb(adb, ["devices"], timeout=6)
    for line in res.stdout.splitlines()[1:]:
        parts = line.strip().split()
        if len(parts) >= 2 and parts[1] == "device":
            return parts[0]
    return None


def run_as_root(adb: str, serial: str, shell_cmd: str) -> subprocess.CompletedProcess:
    """Execute `shell_cmd` as root on MuMu Player (`uid=0`) WITHOUT calling `adb root` (which resets adbd and breaks TCP 9339)."""
    with tempfile.NamedTemporaryFile("w", suffix=".sh", delete=False, encoding="utf-8", newline="\n") as tf:
        tf.write("#!/system/bin/sh\n")
        tf.write(shell_cmd + "\n")
        local_sh = tf.name
    remote_sh = "/sdcard/Download/um_root_cmd.sh"
    try:
        run_adb(adb, ["push", local_sh, remote_sh], serial, timeout=20)
        id_res = run_adb(adb, ["shell", "id -u"], serial)
        if "0" in id_res.stdout.split():
            return run_adb(adb, ["shell", f"sh {remote_sh}; rm -f {remote_sh}"], serial, timeout=60)
        return run_adb(
            adb,
            [
                "shell",
                f"/system/xbin/su -c 'sh {remote_sh}' || /sbin/su -c 'sh {remote_sh}' || /system/bin/su -c 'sh {remote_sh}' || su -c 'sh {remote_sh}'; rm -f {remote_sh}",
            ],
            serial,
            timeout=60,
        )
    finally:
        try:
            os.remove(local_sh)
        except OSError:
            pass


def get_apk_path() -> str | None:
    if os.environ.get("BSL_APK") and os.path.isfile(os.environ["BSL_APK"]):
        return os.path.abspath(os.environ["BSL_APK"])
    for cand in [
        APK_PATH,
        os.path.join(os.path.dirname(__file__), "..", "com.bsl.v68-rev2.apk"),
        os.path.join(os.path.dirname(__file__), "com.bsl.v68-rev2.apk"),
        os.path.join(os.getcwd(), "com.bsl.v68-rev2.apk"),
        os.path.join(os.path.dirname(__file__), "..", "com.bsl.v68.apk"),
        os.path.join(os.path.dirname(__file__), "com.bsl.v68.apk"),
        os.path.join(os.getcwd(), "com.bsl.v68.apk"),
    ]:
        if os.path.isfile(cand):
            return os.path.abspath(cand)
    for d in [os.getcwd(), os.path.dirname(__file__), os.path.join(os.path.dirname(__file__), "..")]:
        if os.path.isdir(d):
            for f in os.listdir(d):
                if f.lower().endswith(".apk") and ("bsl" in f.lower() or "v68" in f.lower()):
                    return os.path.abspath(os.path.join(d, f))
    return None


def build_patched_libg(source_bytes: bytes) -> bytes:
    data = bytearray(source_bytes)
    for offset, orig_hex, patch_hex in LIBG_ARM64_PATCHES:
        orig_bytes = bytes.fromhex(orig_hex)
        patch_bytes = bytes.fromhex(patch_hex)
        actual = bytes(data[offset : offset + len(orig_bytes)])
        if actual != orig_bytes and actual != patch_bytes:
            raise ValueError(
                f"libg.so mismatch at 0x{offset:06x}: expected {orig_hex}, got {actual.hex()}"
            )
        data[offset : offset + len(patch_bytes)] = patch_bytes
    return bytes(data)


def patch_steve_texts(raw_csv: bytes) -> bytes:
    texts = raw_csv.decode("utf-8", errors="replace")
    patched = (
        texts.replace('"TID_SHOTGUN_GIRL","Shelly"', '"TID_SHOTGUN_GIRL","Steve"')
        .replace('"TID_SHOTGUN_GIRL","SHELLY"', '"TID_SHOTGUN_GIRL","STEVE"')
        .replace('"TID_SHELLY","SHELLY"', '"TID_SHELLY","STEVE"')
    )
    return patched.encode("utf-8")


def extract_from_emulator(adb: str, serial: str) -> tuple[bytes | None, bytes | None]:
    """Pull original libg.so and texts.csv directly from MuMu Player via ADB."""
    with tempfile.TemporaryDirectory() as tmp:
        local_libg = os.path.join(tmp, "libg.so")
        r = run_as_root(
            adb,
            serial,
            f"LIBG=$(pm path {PKG} | head -n1 | sed 's/^package://;s|/base\\.apk$|/lib/arm64/libg.so|'); "
            f"if [ -z \"$LIBG\" ] || [ ! -f \"$LIBG\" ]; then LIBG=$(find /data/app -name libg.so 2>/dev/null | grep {PKG} | head -n1); fi; "
            f"if [ -f \"${{LIBG}}.orig\" ]; then echo \"${{LIBG}}.orig\"; else echo \"$LIBG\"; fi",
        )
        dev_libg = r.stdout.strip().splitlines()[-1] if r.stdout.strip() else ""
        if not dev_libg or not dev_libg.startswith("/"):
            return None, None

        run_as_root(adb, serial, f"cp '{dev_libg}' /sdcard/Download/temp_libg.so && chmod 666 /sdcard/Download/temp_libg.so")
        run_adb(adb, ["pull", "/sdcard/Download/temp_libg.so", local_libg], serial)
        run_as_root(adb, serial, "rm -f /sdcard/Download/temp_libg.so")
        if not os.path.isfile(local_libg):
            return None, None
        with open(local_libg, "rb") as f:
            libg_data = f.read()

        local_texts = os.path.join(tmp, "texts.csv")
        r_apk = run_as_root(adb, serial, f"pm path {PKG} | head -n1 | sed 's/^package://'")
        dev_apk = r_apk.stdout.strip().splitlines()[-1] if r_apk.stdout.strip() else ""
        texts_data = b""
        if dev_apk and dev_apk.startswith("/"):
            run_as_root(adb, serial, f"unzip -p '{dev_apk}' assets/localization/texts.csv > /sdcard/Download/temp_texts.csv && chmod 666 /sdcard/Download/temp_texts.csv")
            run_adb(adb, ["pull", "/sdcard/Download/temp_texts.csv", local_texts], serial)
            run_as_root(adb, serial, "rm -f /sdcard/Download/temp_texts.csv")
            if os.path.isfile(local_texts) and os.path.getsize(local_texts) > 100:
                with open(local_texts, "rb") as f:
                    texts_data = f.read()

        return libg_data, texts_data


def ensure_port_redirects(adb: str, serial: str):
    """Ensure port 9339 & 8080 are routed to host, libBSL.c.so redirects to 127.0.0.1, and iptables catches all destination IPs."""
    run_adb(adb, ["reverse", "tcp:9339", "tcp:9339"], serial)
    run_adb(adb, ["reverse", "tcp:8080", "tcp:8080"], serial)
    redirect_cmd = (
        f"iptables -t nat -D OUTPUT -p tcp --dport 9339 -j DNAT --to-destination 127.0.0.1:9339 2>/dev/null || true; "
        f"iptables -t nat -A OUTPUT -p tcp --dport 9339 -j DNAT --to-destination 127.0.0.1:9339 || true; "
        f"iptables -t nat -D OUTPUT -p udp --dport 9339 -j DNAT --to-destination 127.0.0.1:9339 2>/dev/null || true; "
        f"iptables -t nat -A OUTPUT -p udp --dport 9339 -j DNAT --to-destination 127.0.0.1:9339 || true; "
        f"CFG=$(pm path {PKG} | head -n1 | sed 's/^package://;s|/base\\.apk$|/lib/arm64/libBSL.c.so|'); "
        f"if [ -z \"$CFG\" ] || [ ! -f \"$CFG\" ]; then CFG=$(find /data/app -name libBSL.c.so 2>/dev/null | grep {PKG} | head -n1); fi; "
        f"if [ -n \"$CFG\" ] && [ -f \"$CFG\" ]; then echo '{{\"interaction\":{{\"type\":\"script\",\"path\":\"libBSL.68250_64.so\",\"parameters\":{{\"redirectHost\":\"127.0.0.1\",\"redirectPort\":9339,\"offlineBattles\":true}}}}}}' > \"$CFG\"; chmod 755 \"$CFG\"; fi"
    )
    run_as_root(adb, serial, redirect_cmd)


def apply_mumu_patch(remove: bool = False, launch_app: bool = True) -> bool:
    adb = find_adb()
    if not adb:
        print("[MuMuPatch] MuMu ADB not found at standard paths; skipping ADB step.")
        return False
    serial = connect_mumu(adb)
    if not serial:
        print("[MuMuPatch] MuMu Player is not running yet (or ADB not reachable); skipping ADB patch.")
        return False

    print(f"[MuMuPatch] Connected to MuMu device: {serial}")
    ensure_port_redirects(adb, serial)

    if remove:
        restore_cmd = (
            f"rm -rf /data/data/{PKG}/update /data/data/{PKG}/fp_steve_* && "
            f"LIBG=$(pm path {PKG} | head -n1 | sed 's/^package://;s|/base\\.apk$|/lib/arm64/libg.so|'); "
            f"if [ -z \"$LIBG\" ] || [ ! -f \"$LIBG\" ]; then LIBG=$(find /data/app -name libg.so 2>/dev/null | grep {PKG} | head -n1); fi; "
            f"if [ -n \"$LIBG\" ] && [ ! -f \"${{LIBG}}.orig\" ]; then true; "
            f"elif [ -n \"$LIBG\" ] && [ -f \"${{LIBG}}.orig\" ]; then cp \"${{LIBG}}.orig\" \"${{LIBG}}.tmp\" && chmod 755 \"${{LIBG}}.tmp\" && mv -f \"${{LIBG}}.tmp\" \"$LIBG\"; fi"
        )
        run_as_root(adb, serial, restore_cmd)
        print("[MuMuPatch] Restored original libg.so and cleared /data/data/com.bsl.v68/update.")
        return True

    # Check if already patched with current stamp
    chk = run_as_root(
        adb,
        serial,
        f"[ -f /data/data/{PKG}/{PATCH_STAMP} ] && [ ! -d /data/data/{PKG}/update/csv_logic ] && echo PATCH_OK",
    )
    ensure_port_redirects(adb, serial)
    if "PATCH_OK" in chk.stdout:
        print(f"[MuMuPatch] First-Person POV & Steve patch ({PATCH_STAMP}) already installed on MuMu Player!")
        return True

    print("[MuMuPatch] Preparing First-Person libg.so & Steve texts.csv patch...")
    apk_path = get_apk_path()
    raw_libg: bytes | None = None
    raw_texts: bytes | None = None

    if apk_path and os.path.isfile(apk_path):
        print(f"[MuMuPatch] Reading original binaries from APK: {apk_path}")
        with zipfile.ZipFile(apk_path, "r") as z:
            raw_libg = z.read("lib/arm64-v8a/libg.so")
            try:
                raw_texts = z.read("assets/localization/texts.csv")
            except Exception:
                raw_texts = None
    else:
        print("[MuMuPatch] Local APK not found; pulling original libg.so directly from MuMu Player...")
        raw_libg, raw_texts = extract_from_emulator(adb, serial)

    if not raw_libg:
        print("[MuMuPatch] Error: Could not find BSL v68 installed in MuMu Player or any local APK.")
        print("[MuMuPatch] Please install BSL v68 in MuMu Player (or place the APK in this directory) and try again!")
        return False

    libg_bytes = build_patched_libg(raw_libg)
    texts_bytes = patch_steve_texts(raw_texts) if raw_texts else b""

    with tempfile.TemporaryDirectory() as tmp:
        libg_file = os.path.join(tmp, "libg_fp.so")
        with open(libg_file, "wb") as f:
            f.write(libg_bytes)

        print("[MuMuPatch] Pushing patched First-Person libg.so to MuMu Player...")
        run_adb(adb, ["push", libg_file, "/sdcard/Download/libg_fp.so"], serial, timeout=60)

        push_texts_cmd = ""
        if texts_bytes:
            texts_file = os.path.join(tmp, "texts.csv")
            with open(texts_file, "wb") as f:
                f.write(texts_bytes)
            run_adb(adb, ["push", texts_file, "/sdcard/Download/steve_texts.csv"], serial)
            push_texts_cmd = (
                f"mkdir -p /data/data/{PKG}/update/localization /data/data/{PKG}/update/assets/localization && "
                f"cp /sdcard/Download/steve_texts.csv /data/data/{PKG}/update/localization/texts.csv && "
                f"cp /sdcard/Download/steve_texts.csv /data/data/{PKG}/update/assets/localization/texts.csv && "
                f"chmod -R 755 /data/data/{PKG}/update && "
            )

        cmd = (
            f"am force-stop {PKG} >/dev/null 2>&1 || true; "
            f"LIBG=$(pm path {PKG} | head -n1 | sed 's/^package://;s|/base\\.apk$|/lib/arm64/libg.so|'); "
            f"if [ -z \"$LIBG\" ] || [ ! -f \"$LIBG\" ]; then LIBG=$(find /data/app -name libg.so 2>/dev/null | grep {PKG} | head -n1); fi; "
            f"if [ -n \"$LIBG\" ] && [ -f \"$LIBG\" ]; then "
            f"  if [ ! -f \"${{LIBG}}.orig\" ]; then cp \"$LIBG\" \"${{LIBG}}.orig\"; fi; "
            f"  cp /sdcard/Download/libg_fp.so \"${{LIBG}}.tmp\" && chmod 755 \"${{LIBG}}.tmp\" && mv -f \"${{LIBG}}.tmp\" \"$LIBG\"; "
            f"fi && "
            f"rm -rf /data/data/{PKG}/update /data/data/{PKG}/fp_steve_* && "
            f"{push_texts_cmd}"
            f"touch /data/data/{PKG}/{PATCH_STAMP} && "
            f"echo PATCH_INSTALLED_OK"
        )
        r = run_as_root(adb, serial, cmd)
        run_adb(adb, ["reverse", "tcp:9339", "tcp:9339"], serial)
        run_adb(adb, ["reverse", "tcp:8080", "tcp:8080"], serial)
        if launch_app:
            run_adb(adb, ["shell", f"monkey -p {PKG} -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1"], serial)
        if "PATCH_INSTALLED_OK" in r.stdout:
            print("[MuMuPatch] Installed First-Person 3D Camera (W/S/A/D Fixed), Hidden 2D HUD, Hidden Shelly Mesh & Crash-Free Attack Disabler!")
            return True
        else:
            print(f"[MuMuPatch] Warning: Root install returned: {r.stdout.strip()} {r.stderr.strip()}")
            return False


def main():
    apply_mumu_patch(remove="--remove" in sys.argv, launch_app=True)


if __name__ == "__main__":
    main()
