// Forja3D — compilación genérica para proyectos propios (plantilla "basica").
// La llama GitHub Actions / GameCI con -executeMethod Forja3DBuild.BuildAndroid.
// Gancho opcional: si el proyecto define una clase de Editor llamada Forja3DPreparar con un método
// público y estático Preparar(), se ejecuta antes de compilar (p. ej. para generar la escena).
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class Forja3DBuild
{
    const string RutaApp = "ProjectSettings/Forja3DApp.json";
    const string RutaIdentidad = "Assets/Forja3D/App/identidad.json";
    [Serializable] class DatosApp { public string nombre; public string version; public int codigo = 1; }
    [Serializable] class Identidad { public string paquete; public string empresa; }

    static string Arg(string nombre, string porDefecto)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, nombre);
        return i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : porDefecto;
    }

    [MenuItem("Forja3D/Compilar APK")]
    static void Menu() => BuildAndroid();

    public static void BuildAndroid()
    {
        try
        {
            bool aab = Arg("-formato", "apk") == "aab";
            string ext = aab ? ".aab" : ".apk";
            string salida = Arg("-customBuildPath", "build/Android/juego" + ext);
            if (salida.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) || salida.EndsWith(".aab", StringComparison.OrdinalIgnoreCase))
                salida = Path.ChangeExtension(salida, ext);
            else salida = Path.Combine(salida, "juego" + ext);

            var app = File.Exists(RutaApp) ? JsonUtility.FromJson<DatosApp>(File.ReadAllText(RutaApp)) : new DatosApp();
            string nombreApp = string.IsNullOrWhiteSpace(app.nombre) ? PlayerSettings.productName : app.nombre;
            string version = string.IsNullOrWhiteSpace(app.version) ? "0.1.0" : app.version;
            int codigo = Mathf.Max(1, app.codigo);

            Debug.Log("[Forja3D] 1/3 Preparando el proyecto…");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var gancho = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType("Forja3DPreparar"); } catch { return null; } })
                .FirstOrDefault(t => t != null)?
                .GetMethod("Preparar", BindingFlags.Public | BindingFlags.Static);
            if (gancho != null)
            {
                Debug.Log("[Forja3D] Ejecutando Forja3DPreparar.Preparar()…");
                gancho.Invoke(null, null);
                AssetDatabase.SaveAssets();
            }

            Debug.Log("[Forja3D] 2/3 Escenas…");
            var escenas = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();
            if (escenas.Length == 0)
                escenas = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
            if (escenas.Length == 0) throw new Exception("No hay ninguna escena en el proyecto (añade una a Build Settings o crea Forja3DPreparar.Preparar()).");
            Debug.Log("[Forja3D] Escenas: " + string.Join(", ", escenas));

            Debug.Log("[Forja3D] 3/3 Compilando…");
            var t = NamedBuildTarget.Android;
            PlayerSettings.productName = nombreApp;
            var id = File.Exists(RutaIdentidad) ? JsonUtility.FromJson<Identidad>(File.ReadAllText(RutaIdentidad)) : new Identidad();
            if (!string.IsNullOrWhiteSpace(id.empresa)) PlayerSettings.companyName = id.empresa;
            string actual = PlayerSettings.GetApplicationIdentifier(t);
            if (!string.IsNullOrWhiteSpace(id.paquete)) PlayerSettings.SetApplicationIdentifier(t, id.paquete.Trim());
            else if (string.IsNullOrWhiteSpace(actual) || actual.StartsWith("com.DefaultCompany") || actual.StartsWith("com.Unity"))
            {
                var slug = new string(nombreApp.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                if (string.IsNullOrEmpty(slug) || char.IsDigit(slug[0])) slug = "app" + slug;
                PlayerSettings.SetApplicationIdentifier(t, "com.forja3d." + slug);
            }
            PlayerSettings.bundleVersion = version;
            PlayerSettings.Android.bundleVersionCode = codigo;
            PlayerSettings.SetScriptingBackend(t, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            if ((int)PlayerSettings.Android.minSdkVersion < 24) PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            EditorUserBuildSettings.buildAppBundle = aab;
            if (aab)
            {
                string ks = Path.GetFullPath(Arg("-keystorePath", "android.keystore"));
                if (!File.Exists(ks)) throw new Exception("Falta el keystore para firmar el AAB. Agrega los secretos ANDROID_KEYSTORE_BASE64, ANDROID_KEYSTORE_PASS, ANDROID_KEYALIAS_NAME y ANDROID_KEYALIAS_PASS.");
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = ks;
                PlayerSettings.Android.keystorePass = Arg("-keystorePass", "");
                PlayerSettings.Android.keyaliasName = Arg("-keyaliasName", "");
                PlayerSettings.Android.keyaliasPass = Arg("-keyaliasPass", "");
            }
            else PlayerSettings.Android.useCustomKeystore = false;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(salida)));
            var reporte = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = escenas,
                locationPathName = salida,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });
            var r = reporte.summary;
            Debug.Log($"[Forja3D] Formato: {(aab ? "AAB (Google Play)" : "APK")} · API destino 36");
            Debug.Log($"[Forja3D] Resultado: {r.result} · {r.totalSize / (1024f * 1024f):0.0} MB · {r.totalTime} · errores {r.totalErrors}");
            if (r.result != BuildResult.Succeeded)
            {
                foreach (var paso in reporte.steps)
                    foreach (var msg in paso.messages)
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                            Debug.Log($"[Forja3D] ERROR en '{paso.name}': {msg.content.Replace('\n', ' ').Substring(0, Mathf.Min(400, msg.content.Length))}");
                Salir(1);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[Forja3D] ERROR: " + (e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e));
            Salir(1);
        }
    }

    static void Salir(int codigo)
    {
        if (Application.isBatchMode) EditorApplication.Exit(codigo);
        else if (codigo != 0) EditorUtility.DisplayDialog("Forja3D", "La compilación falló. Revisa la consola.", "OK");
    }
}
