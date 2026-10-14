import os, subprocess, shutil, sys

opts = '-XX:MaxMetaspaceSize=384m -XX:+HeapDumpOnOutOfMemoryError -Xms256m -Xmx3G -Dfile.encoding=UTF-8 -XX:-UsePerfData -Dorg.gradle.internal.instrumentation.agent=false'
env = os.environ.copy()
if "JAVA_HOME" not in env:
    for candidate in [r'C:\Program Files\Java\jdk-25.0.4', r'C:\Program Files\Java\jdk-21', r'C:\Program Files\Eclipse Adoptium\jdk-21']:
        if os.path.isdir(candidate):
            env['JAVA_HOME'] = candidate
            print(f"Detected JAVA_HOME: {candidate}")
            break

env['GRADLE_OPTS'] = opts

cmd = [
    os.path.abspath('bsl-minecraft-mod/gradlew.bat'),
    'build',
    '-x', 'test',
    '--no-configuration-cache',
    '--no-daemon',
    '-Dorg.gradle.jvmargs=' + opts,
    '-Dorg.gradle.internal.instrumentation.agent=false'
]

print("Building bsl-minecraft-mod...")
r = subprocess.run(cmd, cwd='bsl-minecraft-mod', env=env)
if r.returncode != 0:
    print(f"Build failed with return code {r.returncode}")
    sys.exit(r.returncode)

print("BUILD SUCCESSFUL!")
jar_src = os.path.abspath('bsl-minecraft-mod/build/libs/passthrough-0.1.0.jar')
print("Jar generated:", jar_src, "size:", os.path.getsize(jar_src))

# Copy to detected Modrinth / Minecraft profiles
appdata = os.environ.get('APPDATA', '')
targets = [
    os.path.join(appdata, 'ModrinthApp', 'profiles', 'Fabric 26.3', 'mods'),
    os.path.join(appdata, '.minecraft', 'mods'),
]
profiles_root = os.path.join(appdata, 'ModrinthApp', 'profiles')
if os.path.isdir(profiles_root):
    for p in os.listdir(profiles_root):
        if '26.3' in p or 'Fabric' in p:
            p_mods = os.path.join(profiles_root, p, 'mods')
            if p_mods not in targets:
                targets.append(p_mods)
for t in targets:
    if os.path.isdir(os.path.dirname(t)):
        os.makedirs(t, exist_ok=True)
        dst = os.path.join(t, 'passthrough-0.1.0.jar')
        try:
            shutil.copy2(jar_src, dst)
            print(f"Copied to: {dst}")
        except Exception as e:
            print(f"Could not copy to {dst}: {e}")
