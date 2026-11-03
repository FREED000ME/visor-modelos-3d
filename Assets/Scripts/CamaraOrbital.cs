// Visor 3D — cámara orbital táctil.
// 1 dedo: girar · pellizco: zoom · 2 dedos: desplazar · doble toque: recentrar.
// En el editor: botón izquierdo gira, derecho desplaza, rueda hace zoom, doble clic recentra.
using UnityEngine;
using UnityEngine.EventSystems;

namespace Visor3D
{
    public class CamaraOrbital : MonoBehaviour
    {
        public Vector3 objetivo = new Vector3(0, 0.5f, 0);
        public float distancia = 4f, yaw = 25f, pitch = 12f;
        public float distMin = 0.5f, distMax = 20f;
        [Tooltip("Altura extra del objetivo (sigue al modelo cuando vuela).")]
        public float desplazamientoY;

        Vector3 objIni; float distIni, yawIni, pitchIni;
        float yawV, pitchV; // inercia
        float ultimoToque = -1f;
        bool ignorar, dosDedos;
        Vector2 prevMedio; float prevDist;
        Vector3 ultimoRaton; float ultimoClic = -1f; bool ratonIgnorado;
        float suavY;

        void Awake() => Input.simulateMouseWithTouches = false;

        public void Configurar(Vector3 centro, float dist)
        {
            objetivo = objIni = centro;
            distancia = distIni = dist;
            yaw = yawIni = 25f; pitch = pitchIni = 12f;
            distMin = dist * 0.25f; distMax = dist * 4f;
            yawV = pitchV = 0f;
        }

        public void Recentrar()
        {
            objetivo = objIni; distancia = distIni; yaw = yawIni; pitch = pitchIni; yawV = pitchV = 0f;
        }

        static bool SobreUI(int id) => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(id);

        void Update()
        {
            if (Input.touchCount > 0) Toques(); else { dosDedos = false; Raton(); }

            // inercia del giro al soltar
            if (Input.touchCount == 0 && !Input.GetMouseButton(0))
            {
                yaw += yawV * Time.unscaledDeltaTime;
                pitch += pitchV * Time.unscaledDeltaTime;
                float k = Mathf.Exp(-6f * Time.unscaledDeltaTime);
                yawV *= k; pitchV *= k;
            }
        }

        void LateUpdate()
        {
            pitch = Mathf.Clamp(pitch, -5f, 85f);
            distancia = Mathf.Clamp(distancia, distMin, distMax);
            suavY = Mathf.Lerp(suavY, desplazamientoY, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
            var r = Quaternion.Euler(pitch, yaw, 0f);
            var centro = objetivo + Vector3.up * suavY;
            transform.SetPositionAndRotation(centro - r * Vector3.forward * distancia, r);
        }

        float GradosPorPixel => 300f / Mathf.Max(1, Mathf.Min(Screen.width, Screen.height));

        void Toques()
        {
            if (Input.touchCount == 1)
            {
                var t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began)
                {
                    dosDedos = false;
                    ignorar = SobreUI(t.fingerId);
                    yawV = pitchV = 0f;
                    if (!ignorar)
                    {
                        if (Time.unscaledTime - ultimoToque < 0.3f) { Recentrar(); ultimoToque = -1f; }
                        else ultimoToque = Time.unscaledTime;
                    }
                }
                if (ignorar || dosDedos) return; // tras un gesto de dos dedos no gira hasta levantar
                if (t.phase == TouchPhase.Moved)
                {
                    float g = GradosPorPixel;
                    yaw += t.deltaPosition.x * g;
                    pitch -= t.deltaPosition.y * g;
                    float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
                    yawV = t.deltaPosition.x * g / dt * 0.5f;
                    pitchV = -t.deltaPosition.y * g / dt * 0.5f;
                    if (t.deltaPosition.sqrMagnitude > 25f) ultimoToque = -1f; // un arrastre no cuenta como toque
                }
                return;
            }

            var a = Input.GetTouch(0); var b = Input.GetTouch(1);
            var medio = (a.position + b.position) * 0.5f;
            float d = Vector2.Distance(a.position, b.position);
            if (!dosDedos || a.phase == TouchPhase.Began || b.phase == TouchPhase.Began)
            {
                dosDedos = true; prevMedio = medio; prevDist = d;
                ignorar = SobreUI(a.fingerId) || SobreUI(b.fingerId);
                yawV = pitchV = 0f; ultimoToque = -1f;
                return;
            }
            if (!ignorar)
            {
                if (prevDist > 1f && d > 1f) distancia *= prevDist / d;
                var dm = medio - prevMedio;
                var cam = GetComponent<Camera>();
                float fov = cam ? cam.fieldOfView : 45f;
                float mundoPorPixel = 2f * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * distancia / Mathf.Max(1, Screen.height);
                objetivo -= (transform.right * dm.x + transform.up * dm.y) * mundoPorPixel;
            }
            prevMedio = medio; prevDist = d;
        }

        void Raton()
        {
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                ratonIgnorado = SobreUI(-1);
                ultimoRaton = Input.mousePosition;
                if (!ratonIgnorado && Input.GetMouseButtonDown(0))
                {
                    if (Time.unscaledTime - ultimoClic < 0.3f) Recentrar();
                    ultimoClic = Time.unscaledTime;
                }
            }
            var delta = Input.mousePosition - ultimoRaton;
            ultimoRaton = Input.mousePosition;
            if (ratonIgnorado) return;
            if (Input.GetMouseButton(0))
            {
                float g = GradosPorPixel;
                yaw += delta.x * g; pitch -= delta.y * g;
            }
            else if (Input.GetMouseButton(1))
            {
                float mundoPorPixel = 2f * Mathf.Tan(22.5f * Mathf.Deg2Rad) * distancia / Mathf.Max(1, Screen.height);
                objetivo -= (transform.right * delta.x + transform.up * delta.y) * mundoPorPixel;
            }
            float rueda = Input.mouseScrollDelta.y;
            if (Mathf.Abs(rueda) > 0.01f && !SobreUI(-1)) distancia *= Mathf.Pow(0.9f, rueda);
        }
    }
}
