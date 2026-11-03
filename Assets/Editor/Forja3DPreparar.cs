// Visor 3D — preparación automática antes de compilar (la llama Forja3DBuild en la nube).
// Por cada carpeta de Assets/Forja3D/Modelos/<nombre>/ (FBX + forja3d.json, las trae Forja3D):
//   material con la textura incrustada, Animator Controller con un estado por clip y un prefab.
// Después genera la escena del visor (cámara, luces, suelo con sombra, VisorApp) y la pone en Build Settings.
// Para añadir un modelo nuevo basta con copiar su carpeta a Assets/Forja3D/Modelos (usar_animacion / guardar_asset).
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Visor3D;

public static class Forja3DPreparar
{
    public const string Raiz = "Assets/Forja3D/Modelos";
    const string Gen = "Assets/Forja3D/Generado";
    const string RutaEscena = Gen + "/Visor.unity";
    static readonly Color Fondo = new Color(0.86f, 0.87f, 0.89f);

    [Serializable] class Arch { public string nombre; public string ext; public string tipo; }
    [Serializable] class ClipM { public string nombre; }
    [Serializable] class Man { public string nombre; public string cuerpo; public string especie; public Arch[] archivos; public ClipM[] clips; }
    [Serializable] class Extra { public string titulo; }

    [MenuItem("Forja3D/Preparar visor")]
    public static void Preparar()
    {
        try
        {
            Directory.CreateDirectory(Gen);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigurarPlayer();

            var entradas = new List<EntradaModelo>();
            if (Directory.Exists(Raiz))
                foreach (var dir in Directory.GetDirectories(Raiz).OrderBy(d => d))
                {
                    try
                    {
                        var e = PrepararModelo(dir.Replace('\\', '/'));
                        if (e != null) entradas.Add(e);
                    }
                    catch (Exception ex) { Debug.LogError($"[Forja3D] No se pudo preparar {dir}: {ex}"); }
                }
            if (entradas.Count == 0) Debug.LogError("[Forja3D] ERROR: no hay modelos en " + Raiz);

            CrearEscena(entradas.ToArray());
            AssetDatabase.SaveAssets();
            Debug.Log($"[Forja3D] Visor listo con {entradas.Count} modelo(s): {string.Join(", ", entradas.Select(x => x.titulo + " [" + string.Join("/", x.clips) + "]"))}");
        }
        catch (Exception e)
        {
            Debug.LogError("[Forja3D] ERROR preparando el visor: " + e);
            throw;
        }
    }

    static void ConfigurarPlayer()
    {
        PlayerSettings.companyName = "Forja3D";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.colorSpace = ColorSpace.Linear;

        // El visor usa el Input Manager clásico (Input.touches): si el proyecto quedó solo con el Input System, se activan ambos.
        var ajustes = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
        if (ajustes != null)
        {
            var so = new SerializedObject(ajustes);
            var p = so.FindProperty("activeInputHandler");
            if (p != null && p.intValue == 1)
            {
                p.intValue = 2;
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("[Forja3D] Input Manager clásico activado (Both).");
            }
        }
    }

    static string Corto(string n)
    {
        if (string.IsNullOrEmpty(n)) return n;
        int i = n.LastIndexOf('|');
        return i >= 0 ? n.Substring(i + 1) : n;
    }

    static string Titulo(string n)
    {
        var s = (n ?? "modelo").Replace('-', ' ').Replace('_', ' ').Trim();
        return s.Length == 0 ? "Modelo" : char.ToUpper(s[0]) + s.Substring(1);
    }

    static EntradaModelo PrepararModelo(string dir)
    {
        string rutaMan = dir + "/forja3d.json";
        if (!File.Exists(rutaMan)) return null;
        var man = JsonUtility.FromJson<Man>(File.ReadAllText(rutaMan)) ?? new Man();
        string n = Path.GetFileName(dir);

        var fbx = Directory.GetFiles(dir, "*.fbx").Select(p => p.Replace('\\', '/'))
            .OrderBy(p => Path.GetFileNameWithoutExtension(p) == n ? 0 : 1).FirstOrDefault();
        if (fbx == null) { Debug.LogWarning($"[Forja3D] {dir}: no hay FBX."); return null; }

        // Por si el FBX se importó antes que el postprocesador
        if (AssetImporter.GetAtPath(fbx) is ModelImporter mi && (mi.animationType != ModelImporterAnimationType.Generic || mi.optimizeGameObjects))
            mi.SaveAndReimport();

        var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (!go) { Debug.LogError($"[Forja3D] ERROR: no se pudo cargar {fbx}"); return null; }

        var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToList();
        var pedidos = (man.clips ?? new ClipM[0]).Select(c => c.nombre).Where(x => !string.IsNullOrEmpty(x)).ToList();
        if (pedidos.Count > 0)
        {
            var filtrados = clips.Where(c => pedidos.Contains(Corto(c.name))).OrderBy(c => pedidos.IndexOf(Corto(c.name))).ToList();
            if (filtrados.Count > 0) clips = filtrados;
        }
        // sin nombres repetidos
        clips = clips.GroupBy(c => Corto(c.name)).Select(g => g.First()).ToList();

        string carpeta = $"{Gen}/{n}";
        Directory.CreateDirectory(carpeta);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        AnimatorController ctrl = null;
        if (clips.Count > 0)
        {
            string rutaCtrl = $"{carpeta}/{n}.controller";
            AssetDatabase.DeleteAsset(rutaCtrl);
            ctrl = AnimatorController.CreateAnimatorControllerAtPath(rutaCtrl);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var c in clips)
            {
                var st = sm.AddState(Corto(c.name));
                st.motion = c;
                st.writeDefaultValues = true;
            }
        }

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(go);
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        inst.name = n;
        var mat = MaterialEmbebido(carpeta, fbx);
        foreach (var r in inst.GetComponentsInChildren<Renderer>())
        {
            if (mat) r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }
        var anim = inst.GetComponent<Animator>();
        if (!anim) anim = inst.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (man.cuerpo == "ave" && !inst.GetComponent<AveProcedural>()) inst.AddComponent<AveProcedural>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(inst, $"{carpeta}/{n}.prefab");
        UnityEngine.Object.DestroyImmediate(inst);

        string titulo = null;
        var rutaExtra = dir + "/visor.json";
        if (File.Exists(rutaExtra)) { try { titulo = JsonUtility.FromJson<Extra>(File.ReadAllText(rutaExtra))?.titulo; } catch { } }

        Debug.Log($"[Forja3D] Modelo {n}: {clips.Count} clip(s), material {(mat ? "con textura" : "del FBX")}");
        return new EntradaModelo
        {
            nombre = n,
            titulo = string.IsNullOrWhiteSpace(titulo) ? Titulo(n) : titulo,
            cuerpo = man.cuerpo,
            prefab = prefab,
            clips = clips.Select(c => Corto(c.name)).ToArray(),
        };
    }

    static void CrearEscena(EntradaModelo[] entradas)
    {
        var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Iluminación y niebla (en la escena para que la compilación conserve sus variantes de shader)
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.78f, 0.81f, 0.86f);
        RenderSettings.ambientEquatorColor = new Color(0.62f, 0.63f, 0.66f);
        RenderSettings.ambientGroundColor = new Color(0.36f, 0.34f, 0.32f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Fondo;
        RenderSettings.fogStartDistance = 9f;
        RenderSettings.fogEndDistance = 28f;

        var camGo = new GameObject("Camara", typeof(Camera), typeof(CamaraOrbital));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Fondo;
        cam.fieldOfView = 45f;
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 80f;
        cam.allowMSAA = true;
        camGo.transform.position = new Vector3(0, 1.5f, -4f);

        var sol = new GameObject("Sol", typeof(Light)).GetComponent<Light>();
        sol.type = LightType.Directional;
        sol.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
        sol.color = new Color(1f, 0.97f, 0.92f);
        sol.intensity = 1.05f;
        sol.shadows = LightShadows.Soft;
        sol.shadowStrength = 0.55f;
        sol.shadowBias = 0.02f;
        sol.shadowNormalBias = 0.2f;

        var relleno = new GameObject("Relleno", typeof(Light)).GetComponent<Light>();
        relleno.type = LightType.Directional;
        relleno.transform.rotation = Quaternion.Euler(25f, 150f, 0f);
        relleno.color = new Color(0.85f, 0.9f, 1f);
        relleno.intensity = 0.35f;
        relleno.shadows = LightShadows.None;

        var suelo = GameObject.CreatePrimitive(PrimitiveType.Plane);
        suelo.name = "Suelo";
        suelo.transform.localScale = new Vector3(5f, 1f, 5f);
        var rutaMatSuelo = Gen + "/Suelo.mat";
        AssetDatabase.DeleteAsset(rutaMatSuelo);
        var ms = new Material(Shader.Find("Standard")) { name = "Suelo" };
        ms.color = new Color(0.8f, 0.81f, 0.83f);
        ms.SetFloat("_Glossiness", 0.12f);
        ms.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(ms, rutaMatSuelo);
        var rs = suelo.GetComponent<MeshRenderer>();
        rs.sharedMaterial = ms;
        rs.shadowCastingMode = ShadowCastingMode.Off;
        rs.receiveShadows = true;

        var visor = new GameObject("Visor").AddComponent<VisorApp>();
        visor.modelos = entradas;
        visor.camara = camGo.GetComponent<CamaraOrbital>();

        EditorSceneManager.MarkSceneDirty(escena);
        if (!EditorSceneManager.SaveScene(escena, RutaEscena)) throw new Exception("No se pudo guardar la escena " + RutaEscena);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(RutaEscena, true) };
    }

    // Busca imágenes JPG/PNG incrustadas en el FBX binario (Meshy) y crea un material Standard con color + relieve.
    static Material MaterialEmbebido(string carpeta, string fbx)
    {
        string dir = $"{carpeta}/Texturas";
        string rutaMat = $"{dir}/{Path.GetFileNameWithoutExtension(fbx)}.mat";
        var previo = AssetDatabase.LoadAssetAtPath<Material>(rutaMat);
        if (previo) return previo;
        byte[] d;
        try { d = File.ReadAllBytes(fbx); } catch { return null; }
        var imagenes = new List<(byte[] datos, string ext)>();
        void Buscar(byte[] firma, string ext)
        {
            for (int i = 5; i < d.Length - firma.Length; i++)
            {
                bool ok = true;
                for (int k = 0; k < firma.Length; k++) if (d[i + k] != firma[k]) { ok = false; break; }
                if (!ok || d[i - 5] != (byte)'R') continue;
                int len = BitConverter.ToInt32(d, i - 4);
                if (len <= 0 || i + len > d.Length) continue;
                var b = new byte[len]; Buffer.BlockCopy(d, i, b, 0, len);
                imagenes.Add((b, ext)); i += len - 1;
            }
        }
        Buscar(new byte[] { 0xFF, 0xD8, 0xFF }, "jpg");
        Buscar(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "png");
        if (imagenes.Count == 0) return null;

        byte[] albedo = null, normal = null; string extA = "jpg", extN = "jpg";
        foreach (var (datos, ext) in imagenes)
        {
            var t = new Texture2D(2, 2);
            if (!t.LoadImage(datos)) { UnityEngine.Object.DestroyImmediate(t); continue; }
            float r = 0, g = 0, bl = 0, mx = 0; int c = 0;
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                {
                    var p = t.GetPixel(x * t.width / 16, y * t.height / 16);
                    r += p.r; g += p.g; bl += p.b; c++;
                    mx = Mathf.Max(mx, Mathf.Abs(p.r - p.g) + Mathf.Abs(p.g - p.b));
                }
            r /= c; g /= c; bl /= c;
            UnityEngine.Object.DestroyImmediate(t);
            bool esNormal = bl > 0.8f && Mathf.Abs(r - 0.5f) < 0.15f && Mathf.Abs(g - 0.5f) < 0.15f;
            bool gris = mx < 0.04f;
            if (esNormal) { if (normal == null) { normal = datos; extN = ext; } }
            else if (!gris && albedo == null) { albedo = datos; extA = ext; }
        }
        if (albedo == null) foreach (var (datos, ext) in imagenes) if (datos != normal) { albedo = datos; extA = ext; break; }
        if (albedo == null) return null;

        Directory.CreateDirectory(dir);
        string rutaA = $"{dir}/color.{extA}", rutaN = $"{dir}/normal.{extN}";
        File.WriteAllBytes(rutaA, albedo);
        if (normal != null) File.WriteAllBytes(rutaN, normal);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(rutaA) is TextureImporter ia) { ia.maxTextureSize = 2048; ia.sRGBTexture = true; ia.SaveAndReimport(); }
        if (normal != null && AssetImporter.GetAtPath(rutaN) is TextureImporter inn) { inn.textureType = TextureImporterType.NormalMap; inn.maxTextureSize = 1024; inn.SaveAndReimport(); }

        var mat = new Material(Shader.Find("Standard")) { name = Path.GetFileName(carpeta) };
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(rutaA);
        mat.color = Color.white;
        if (normal != null) { mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(rutaN)); mat.EnableKeyword("_NORMALMAP"); mat.SetFloat("_BumpScale", 1f); }
        mat.SetFloat("_Metallic", 0f); mat.SetFloat("_Glossiness", 0.3f);
        mat.enableInstancing = true;
        AssetDatabase.CreateAsset(mat, rutaMat);
        return mat;
    }
}

// Importa los FBX de Assets/Forja3D/Modelos: rig genérico (esqueleto de Forja Animación), clips con nombre corto y en bucle.
public class Visor3DPostprocesador : AssetPostprocessor
{
    public override uint GetVersion() => 2;

    bool Nuestro => assetPath.StartsWith(Forja3DPreparar.Raiz + "/") && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

    void OnPreprocessModel()
    {
        if (!Nuestro) return;
        var mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Generic;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.optimizeGameObjects = false; // AveProcedural mueve los huesos por su nombre
        mi.importAnimation = true;
        mi.bakeAxisConversion = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.isReadable = false;
    }

    void OnPreprocessAnimation()
    {
        if (!Nuestro) return;
        var mi = (ModelImporter)assetImporter;
        var cs = mi.defaultClipAnimations;
        if (cs == null || cs.Length == 0) return;
        foreach (var c in cs)
        {
            int i = c.name.LastIndexOf('|');
            if (i >= 0) c.name = c.name.Substring(i + 1);
            c.loopTime = true;
            c.loopPose = false;
        }
        mi.clipAnimations = cs;
    }
}
#endif
