// Visor 3D — app principal: carga los modelos, arma la interfaz (uGUI creada por código) y controla las animaciones.
// La lista de modelos la rellena Forja3DPreparar al compilar: cada carpeta de Assets/Forja3D/Modelos es un modelo.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Visor3D
{
    [Serializable]
    public class EntradaModelo
    {
        public string nombre;
        public string titulo;
        public string cuerpo;
        public GameObject prefab;
        public string[] clips;
    }

    public class VisorApp : MonoBehaviour
    {
        public EntradaModelo[] modelos = new EntradaModelo[0];
        public CamaraOrbital camara;
        [Tooltip("Tamaño (unidades) al que se ajusta la dimensión mayor de cada modelo.")]
        public float tamanoObjetivo = 2f;

        class Accion { public string id, titulo; public AveProcedural.Modo proc; public bool vuela; public Image fondo; public Text texto; }

        static readonly Color ColPanel = new Color(1f, 1f, 1f, 0.9f);
        static readonly Color ColBoton = new Color(0.92f, 0.93f, 0.95f, 1f);
        static readonly Color ColAcento = new Color(0.12f, 0.44f, 0.86f, 1f);
        static readonly Color ColTexto = new Color(0.13f, 0.15f, 0.19f, 1f);

        Font fuente;
        Transform pivote;
        GameObject actual;
        Animator anim;
        AveProcedural proc;
        int indiceModelo = -1, indiceAccion = -1;
        readonly List<Accion> acciones = new List<Accion>();
        readonly List<(Image fondo, Text texto)> botonesModelo = new List<(Image, Text)>();
        float velocidad = 1f, alturaVuelo, tiempoVuelo, alto = 1f;
        bool pausado, vuela;

        RectTransform seguro, filaAcciones, contenidoModelos;
        Text titulo, ayuda, textoVelocidad, textoPausa;
        Rect ultimaArea;
        float inicio;

        void Start()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.shadowDistance = 12f;
            QualitySettings.shadowCascades = 1;
            QualitySettings.antiAliasing = 2;
            Screen.autorotateToPortrait = Screen.autorotateToLandscapeLeft = Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!camara) camara = FindAnyObjectByType<CamaraOrbital>();
            pivote = new GameObject("Pivote").transform;
            inicio = Time.unscaledTime;

            CrearUI();
            if (modelos == null || modelos.Length == 0)
            {
                titulo.text = "Sin modelos";
                Debug.LogError("[Forja3D] El visor no tiene modelos.");
                return;
            }
            CargarModelo(0);
        }

        // ------------------------------------------------------------------ modelos y animaciones
        void CargarModelo(int i)
        {
            if (i < 0 || i >= modelos.Length || modelos[i].prefab == null) return;
            if (actual) Destroy(actual);
            indiceModelo = i;
            var e = modelos[i];

            pivote.position = Vector3.zero;
            actual = Instantiate(e.prefab, pivote);
            actual.transform.localPosition = Vector3.zero;
            actual.transform.localRotation = Quaternion.identity;
            actual.transform.localScale = Vector3.one;
            var b = Limites(actual);
            float max = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            actual.transform.localScale = Vector3.one * (max > 1e-5f ? tamanoObjetivo / max : 1f);
            b = Limites(actual);
            actual.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            alto = Mathf.Max(b.size.y, 0.1f);

            anim = actual.GetComponent<Animator>();
            proc = actual.GetComponent<AveProcedural>();
            if (proc) proc.altoSalto = tamanoObjetivo * 0.3f;
            alturaVuelo = 0f;
            if (camara) { camara.Configurar(new Vector3(0f, alto * 0.5f, 0f), tamanoObjetivo * 1.7f); camara.desplazamientoY = 0f; }

            titulo.text = string.IsNullOrEmpty(e.titulo) ? e.nombre : e.titulo;
            for (int k = 0; k < botonesModelo.Count; k++) Marcar(botonesModelo[k].fondo, botonesModelo[k].texto, k == i);

            // Acciones: para aves, reposo / clips / picoteo / salto
            acciones.Clear();
            bool ave = e.cuerpo == "ave" && proc;
            if (ave) acciones.Add(new Accion { id = "reposo", titulo = "Reposo", proc = AveProcedural.Modo.Reposo });
            foreach (var c in e.clips ?? new string[0])
                acciones.Add(new Accion { id = c, titulo = TituloClip(c), vuela = ave });
            if (ave)
            {
                acciones.Add(new Accion { id = "picoteo", titulo = "Picoteo", proc = AveProcedural.Modo.Picoteo });
                acciones.Add(new Accion { id = "salto", titulo = "Salto", proc = AveProcedural.Modo.Salto });
            }
            for (int k = filaAcciones.childCount - 1; k >= 0; k--) Destroy(filaAcciones.GetChild(k).gameObject);
            for (int k = 0; k < acciones.Count; k++)
            {
                int idx = k;
                var (_, fondo, texto) = Boton(filaAcciones, acciones[k].titulo, () => Reproducir(idx), -1f, 34);
                acciones[k].fondo = fondo; acciones[k].texto = texto;
            }
            indiceAccion = -1;
            if (acciones.Count > 0) Reproducir(0);
            AplicarVelocidad();
        }

        static string TituloClip(string c)
        {
            switch (c)
            {
                case "volar": return "Aleteo";
                case "planear": return "Planeo";
                case "cernirse": return "Cernirse";
                case "intermitente": return "Vuelo mixto";
                case "caminar": return "Caminar";
                case "correr": return "Correr";
                case "idle": return "Reposo";
                default: return string.IsNullOrEmpty(c) ? "?" : char.ToUpper(c[0]) + c.Substring(1).Replace('_', ' ');
            }
        }

        void Reproducir(int k)
        {
            if (k < 0 || k >= acciones.Count) return;
            indiceAccion = k;
            var a = acciones[k];
            for (int i = 0; i < acciones.Count; i++) Marcar(acciones[i].fondo, acciones[i].texto, i == k);
            vuela = a.vuela;
            if (a.proc != AveProcedural.Modo.Nada)
            {
                if (anim) anim.enabled = false;
                if (proc) { proc.Restaurar(); proc.tiempo = 0f; proc.modo = a.proc; }
            }
            else
            {
                if (proc) { proc.modo = AveProcedural.Modo.Nada; proc.Restaurar(); }
                if (anim && anim.runtimeAnimatorController)
                {
                    anim.enabled = true;
                    anim.Play(a.id, 0, 0f);
                }
            }
            AplicarVelocidad();
        }

        void AplicarVelocidad()
        {
            float v = pausado ? 0f : velocidad;
            if (anim) anim.speed = v;
            if (proc) proc.velocidad = v;
            if (textoPausa) textoPausa.text = pausado ? "Reproducir" : "Pausa";
            if (textoVelocidad) textoVelocidad.text = velocidad.ToString("0.00") + "x";
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Application.Quit();
            AjustarAreaSegura();
            if (ayuda)
            {
                float t = Time.unscaledTime - inicio;
                var c = ayuda.color; c.a = Mathf.Clamp01(1f - (t - 7f) / 1.5f); ayuda.color = c;
            }
            if (!actual) return;

            float dt = Time.deltaTime;
            float objetivo = vuela ? alto * 0.9f : 0f;
            alturaVuelo = Mathf.Lerp(alturaVuelo, objetivo, 1f - Mathf.Exp(-3f * dt));
            if (vuela && !pausado) tiempoVuelo += dt * velocidad;
            float vaiven = vuela ? Mathf.Sin(tiempoVuelo * 2f * Mathf.PI * 0.45f) * 0.04f * tamanoObjetivo : 0f;
            float salto = proc ? proc.Elevacion : 0f;
            pivote.position = new Vector3(0f, alturaVuelo + vaiven + salto, 0f);
            if (camara) camara.desplazamientoY = alturaVuelo;
        }

        static Bounds Limites(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------------ interfaz
        void CrearUI()
        {
            if (!FindAnyObjectByType<EventSystem>())
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var cgo = new GameObject("UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var cs = cgo.GetComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1080, 1920);
            cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            cs.matchWidthOrHeight = 0.5f;

            seguro = Rect("AreaSegura", cgo.transform);
            Estirar(seguro);

            // Título y ayuda
            titulo = Texto(seguro, "", 56, FontStyle.Bold, TextAnchor.MiddleCenter);
            var rt = titulo.rectTransform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 110); rt.anchoredPosition = new Vector2(0, -20);
            ayuda = Texto(seguro, "1 dedo: girar · pellizco: zoom · 2 dedos: mover · doble toque: centrar", 28, FontStyle.Normal, TextAnchor.MiddleCenter);
            rt = ayuda.rectTransform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(-40, 60); rt.anchoredPosition = new Vector2(0, -125);
            ayuda.color = new Color(ColTexto.r, ColTexto.g, ColTexto.b, 0.75f);

            // Panel inferior
            var panel = Rect("Panel", seguro);
            panel.anchorMin = new Vector2(0, 0); panel.anchorMax = new Vector2(1, 0); panel.pivot = new Vector2(0.5f, 0);
            panel.sizeDelta = new Vector2(0, 430); panel.anchoredPosition = Vector2.zero;
            panel.gameObject.AddComponent<Image>().color = ColPanel;
            var vl = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(24, 24, 22, 24); vl.spacing = 16;
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;

            // Fila 1: animaciones
            filaAcciones = Fila(panel, 110, 12, true);

            // Fila 2: pausa + velocidad
            var ctrl = Fila(panel, 100, 18, false);
            var (_, fp, tp) = Boton(ctrl, "Pausa", () => { pausado = !pausado; AplicarVelocidad(); }, 280f, 34);
            textoPausa = tp; fp.color = ColBoton;
            CrearSlider(ctrl);
            textoVelocidad = Texto(ctrl, "1.00x", 36, FontStyle.Bold, TextAnchor.MiddleRight);
            var le = textoVelocidad.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 130; le.minWidth = 130;

            // Fila 3: selector de modelos (desplazable para cuando haya muchos)
            var scrollGo = Rect("Modelos", panel);
            var leS = scrollGo.gameObject.AddComponent<LayoutElement>(); leS.minHeight = leS.preferredHeight = 120;
            var sr = scrollGo.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = true; sr.vertical = false; sr.movementType = ScrollRect.MovementType.Elastic;
            var vista = Rect("Vista", scrollGo); Estirar(vista);
            vista.gameObject.AddComponent<RectMask2D>();
            vista.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f); // recibe el arrastre
            contenidoModelos = Rect("Contenido", vista);
            contenidoModelos.anchorMin = new Vector2(0, 0); contenidoModelos.anchorMax = new Vector2(0, 1); contenidoModelos.pivot = new Vector2(0, 0.5f);
            contenidoModelos.sizeDelta = Vector2.zero;
            var hl = contenidoModelos.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 12; hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
            var fit = contenidoModelos.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vista; sr.content = contenidoModelos;
            for (int i = 0; i < (modelos?.Length ?? 0); i++)
            {
                int idx = i;
                var e = modelos[i];
                var (_, f, t) = Boton(contenidoModelos, (string.IsNullOrEmpty(e.titulo) ? e.nombre : e.titulo), () => CargarModelo(idx), 360f, 34);
                if (e.cuerpo != "ave") t.text = string.IsNullOrEmpty(e.titulo) ? e.nombre : e.titulo;
                botonesModelo.Add((f, t));
            }
        }

        void AjustarAreaSegura()
        {
            var a = Screen.safeArea;
            if (a == ultimaArea || Screen.width <= 0 || Screen.height <= 0) return;
            ultimaArea = a;
            seguro.anchorMin = new Vector2(a.xMin / Screen.width, a.yMin / Screen.height);
            seguro.anchorMax = new Vector2(a.xMax / Screen.width, a.yMax / Screen.height);
            seguro.offsetMin = seguro.offsetMax = Vector2.zero;
        }

        static void Marcar(Image fondo, Text texto, bool activo)
        {
            if (fondo) fondo.color = activo ? ColAcento : ColBoton;
            if (texto) texto.color = activo ? Color.white : ColTexto;
        }

        static RectTransform Rect(string n, Transform padre)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(padre, false);
            return (RectTransform)go.transform;
        }

        static void Estirar(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static RectTransform Fila(RectTransform padre, float alto, float espacio, bool expandir)
        {
            var f = Rect("Fila", padre);
            var le = f.gameObject.AddComponent<LayoutElement>(); le.minHeight = le.preferredHeight = alto;
            var hl = f.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = espacio; hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = true; hl.childControlHeight = true;
            hl.childForceExpandWidth = expandir; hl.childForceExpandHeight = true;
            return f;
        }

        Text Texto(Transform padre, string s, int tam, FontStyle estilo, TextAnchor alineado)
        {
            var go = new GameObject("Texto", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(padre, false);
            var t = go.GetComponent<Text>();
            t.font = fuente; t.text = s; t.fontSize = tam; t.fontStyle = estilo; t.alignment = alineado; t.color = ColTexto;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 18; t.resizeTextMaxSize = tam;
            t.raycastTarget = false;
            return t;
        }

        (Button, Image, Text) Boton(Transform padre, string s, Action alPulsar, float ancho, int tam)
        {
            var go = new GameObject(s, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(padre, false);
            var img = go.GetComponent<Image>(); img.color = ColBoton;
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 90;
            if (ancho > 0) { le.preferredWidth = ancho; le.minWidth = ancho; le.flexibleWidth = 0; } else le.flexibleWidth = 1;
            var b = go.GetComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors; cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f); cb.highlightedColor = Color.white; b.colors = cb;
            b.onClick.AddListener(() => alPulsar());
            var t = Texto(go.transform, s, tam, FontStyle.Bold, TextAnchor.MiddleCenter);
            var rt = t.rectTransform; Estirar(rt); rt.offsetMin = new Vector2(8, 4); rt.offsetMax = new Vector2(-8, -4);
            return (b, img, t);
        }

        void CrearSlider(Transform padre)
        {
            var go = new GameObject("Velocidad", typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
            go.transform.SetParent(padre, false);
            var le = go.GetComponent<LayoutElement>(); le.flexibleWidth = 1; le.minHeight = 90; le.minWidth = 200;

            var fondo = new GameObject("Fondo", typeof(RectTransform), typeof(Image));
            fondo.transform.SetParent(go.transform, false);
            fondo.GetComponent<Image>().color = new Color(0.82f, 0.84f, 0.88f, 1f);
            var rf = (RectTransform)fondo.transform;
            rf.anchorMin = new Vector2(0, 0.5f); rf.anchorMax = new Vector2(1, 0.5f); rf.sizeDelta = new Vector2(0, 14);

            var area = Rect("AreaRelleno", go.transform);
            area.anchorMin = new Vector2(0, 0.5f); area.anchorMax = new Vector2(1, 0.5f);
            area.offsetMin = new Vector2(0, -7); area.offsetMax = new Vector2(0, 7);
            var relleno = new GameObject("Relleno", typeof(RectTransform), typeof(Image));
            relleno.transform.SetParent(area, false);
            relleno.GetComponent<Image>().color = ColAcento;
            var rr = (RectTransform)relleno.transform; rr.sizeDelta = Vector2.zero;

            var areaMango = Rect("AreaMango", go.transform);
            Estirar(areaMango); areaMango.offsetMin = new Vector2(22, 0); areaMango.offsetMax = new Vector2(-22, 0);
            var mango = new GameObject("Mango", typeof(RectTransform), typeof(Image));
            mango.transform.SetParent(areaMango, false);
            var im = mango.GetComponent<Image>(); im.color = ColAcento;
            var rm = (RectTransform)mango.transform; rm.sizeDelta = new Vector2(44, -30);

            var s = go.GetComponent<Slider>();
            s.fillRect = rr; s.handleRect = rm; s.targetGraphic = im;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = 0.25f; s.maxValue = 2f; s.value = 1f;
            s.onValueChanged.AddListener(v =>
            {
                velocidad = Mathf.Round(v * 20f) / 20f;
                AplicarVelocidad();
            });
        }
    }
}
