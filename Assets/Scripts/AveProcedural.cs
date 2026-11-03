// Visor 3D — movimientos de ave hechos por código (sin clip): reposo, picoteo y salto.
// Trabaja sobre el esqueleto de Forja Animación (torso, pecho, cuello, cabeza, cola_raiz, ala_L/R_1..3).
// Los ejes del ave se miden de la propia malla (alas = eje lateral, cabeza = adelante), así no depende
// de cómo se exportó el FBX.
using UnityEngine;

namespace Visor3D
{
    [DefaultExecutionOrder(100)]
    public class AveProcedural : MonoBehaviour
    {
        public enum Modo { Nada, Reposo, Picoteo, Salto }

        public Modo modo = Modo.Nada;
        public float velocidad = 1f;
        public float tiempo;
        [Tooltip("Altura del salto en unidades de mundo.")]
        public float altoSalto = 0.4f;

        /// <summary>Altura extra del cuerpo (salto). La aplica el visor al pivote del modelo.</summary>
        public float Elevacion { get; private set; }

        class Ala { public Transform[] b = new Transform[3]; public Vector3[] eje = new Vector3[3]; public float lado = 1f; }

        Transform[] todos; Quaternion[] rot0; Vector3[] pos0;
        Transform torso, cuello, cabeza, cola;
        Ala[] alas;
        Vector3 fL, uL, rL; // adelante, arriba y derecha del ave en el espacio local de la raíz

        void Awake()
        {
            todos = GetComponentsInChildren<Transform>(true);
            rot0 = new Quaternion[todos.Length]; pos0 = new Vector3[todos.Length];
            for (int i = 0; i < todos.Length; i++) { rot0[i] = todos[i].localRotation; pos0[i] = todos[i].localPosition; }

            torso = Buscar("torso"); cuello = Buscar("cuello"); cabeza = Buscar("cabeza"); cola = Buscar("cola_raiz");
            alas = new[] { CrearAla("L"), CrearAla("R") };

            // Eje lateral: de una punta de ala a la otra. Adelante: perpendicular, hacia la cabeza.
            Vector3 up = Vector3.up;
            Vector3 lateral = transform.right;
            var pa = Punta(alas[0]); var pb = Punta(alas[1]);
            if (pa.HasValue && pb.HasValue && (pb.Value - pa.Value).sqrMagnitude > 1e-8f)
                lateral = Vector3.ProjectOnPlane(pb.Value - pa.Value, up).normalized;
            Vector3 adelante = Vector3.Cross(lateral, up).normalized;
            if (torso && cabeza)
            {
                var haciaCabeza = Vector3.ProjectOnPlane(cabeza.position - torso.position, up);
                var hijo = cabeza.childCount > 0 ? cabeza.GetChild(0) : null;
                if (hijo) haciaCabeza += Vector3.ProjectOnPlane(hijo.position - cabeza.position, up) * 2f;
                if (Vector3.Dot(adelante, haciaCabeza) < 0f) adelante = -adelante;
            }
            Vector3 derecha = Vector3.Cross(up, adelante);
            fL = transform.InverseTransformDirection(adelante);
            uL = transform.InverseTransformDirection(up);
            rL = transform.InverseTransformDirection(derecha);

            Vector3 centro = torso ? torso.position : transform.position;
            foreach (var a in alas)
                if (a.b[0]) a.lado = Mathf.Sign(Vector3.Dot(a.b[0].position - centro, derecha));
        }

        Transform Buscar(string n)
        {
            foreach (var t in todos)
            {
                var x = t.name;
                if (x == n || x.EndsWith(":" + n) || x.EndsWith("|" + n)) return t;
            }
            return null;
        }

        Ala CrearAla(string s)
        {
            var a = new Ala();
            for (int i = 0; i < 3; i++) a.b[i] = Buscar($"ala_{s}_{i + 1}");
            for (int i = 0; i < 3; i++)
            {
                var b = a.b[i]; if (!b) continue;
                Vector3 dir;
                if (i < 2 && a.b[i + 1]) dir = a.b[i + 1].position - b.position;
                else if (b.childCount > 0) dir = b.GetChild(0).position - b.position;
                else if (i > 0 && a.b[i - 1]) dir = b.position - a.b[i - 1].position;
                else dir = b.rotation * Vector3.up;
                if (dir.sqrMagnitude < 1e-10f) dir = b.rotation * Vector3.up;
                a.eje[i] = Quaternion.Inverse(b.rotation) * dir.normalized;
            }
            return a;
        }

        static Vector3? Punta(Ala a)
        {
            for (int i = 2; i >= 0; i--) if (a.b[i]) return a.b[i].childCount > 0 ? a.b[i].GetChild(0).position : a.b[i].position;
            return null;
        }

        /// <summary>Vuelve a la pose de reposo del FBX (antes de pasar el control al Animator).</summary>
        public void Restaurar()
        {
            if (todos == null) return;
            for (int i = 0; i < todos.Length; i++)
            {
                if (todos[i] == transform) continue; // la raíz la coloca el visor
                todos[i].localRotation = rot0[i]; todos[i].localPosition = pos0[i];
            }
        }

        void LateUpdate()
        {
            if (modo == Modo.Nada) { Elevacion = 0f; return; }
            tiempo += Time.deltaTime * velocidad;
            Restaurar();

            Vector3 F = transform.TransformDirection(fL), U = transform.TransformDirection(uL), R = transform.TransformDirection(rL);
            float pliegue = 1f, cabeceo = 0f, giroCabeza = 0f, inclinacion = 0f, colaAng = 0f, aleteo = 0f;
            float elev = 0f;
            const float PI = Mathf.PI;

            switch (modo)
            {
                case Modo.Reposo:
                {
                    float t = tiempo;
                    giroCabeza = Mirada(t) * 35f;
                    cabeceo = Mathf.Sin(t * 1.3f) * 4f + Mathf.Sin(t * 0.37f) * 3f;
                    inclinacion = Mathf.Sin(t * 2f * PI * 0.3f) * 1.5f; // respiración
                    colaAng = Mathf.Sin(t * 0.9f) * 3f + Pulso(t, 3.7f) * 14f;
                    break;
                }
                case Modo.Picoteo:
                {
                    const float T = 1.5f;
                    float p = (tiempo % T) / T;
                    float k = Mathf.Max(Golpe(p, 0.08f, 0.32f), 0.8f * Golpe(p, 0.40f, 0.62f));
                    cabeceo = 45f * k;
                    inclinacion = 22f * k;
                    giroCabeza = Mirada(tiempo * 0.6f) * 15f * (1f - k);
                    colaAng = -12f * k;
                    break;
                }
                case Modo.Salto:
                {
                    const float T = 1.6f;
                    float p = (tiempo % T) / T;
                    if (p < 0.18f)
                    {   // se agacha
                        float x = Mathf.Sin(PI * p / 0.18f);
                        elev = -0.06f * altoSalto * x; inclinacion = 14f * x; colaAng = 6f * x;
                    }
                    else if (p < 0.68f)
                    {   // en el aire: abre las alas y aletea
                        float x = (p - 0.18f) / 0.5f, s = Mathf.Sin(PI * x);
                        elev = altoSalto * 4f * x * (1f - x);
                        pliegue = 1f - 0.85f * s; aleteo = s;
                        inclinacion = -8f * s; colaAng = 14f * s; cabeceo = -6f * s;
                    }
                    else if (p < 0.84f)
                    {   // aterriza
                        float x = Mathf.Sin(PI * (p - 0.68f) / 0.16f);
                        elev = -0.05f * altoSalto * x; inclinacion = 10f * x; colaAng = -8f * x;
                    }
                    else giroCabeza = Mathf.Sin((p - 0.84f) / 0.16f * PI) * 12f;
                    break;
                }
            }
            Elevacion = elev;

            var qT = Quaternion.AngleAxis(inclinacion, R); // positivo = se inclina hacia adelante
            if (torso) torso.rotation = qT * torso.rotation;
            Vector3 F2 = qT * F, U2 = qT * U;
            foreach (var a in alas) Plegar(a, pliegue, F2, U2, R, aleteo);
            if (cuello) cuello.rotation = Quaternion.AngleAxis(giroCabeza * 0.4f, U) * Quaternion.AngleAxis(cabeceo * 0.6f, R) * cuello.rotation;
            if (cabeza) cabeza.rotation = Quaternion.AngleAxis(giroCabeza * 0.6f, U) * Quaternion.AngleAxis(cabeceo * 0.4f, R) * cabeza.rotation;
            if (cola) cola.rotation = Quaternion.AngleAxis(colaAng, R) * cola.rotation;
        }

        // Pliegue en Z, como un ave posada: brazo hacia atrás, antebrazo hacia adelante, mano hacia atrás.
        void Plegar(Ala a, float k, Vector3 F, Vector3 U, Vector3 R, float flap)
        {
            if (!a.b[0]) return;
            Vector3 S = R * a.lado;
            if (k > 0.001f)
            {
                for (int i = 0; i < 3; i++)
                {
                    var b = a.b[i]; if (!b) continue;
                    Vector3 obj = i == 0 ? (-F * 0.75f - U * 0.45f + S * 0.35f)
                                : i == 1 ? (F * 0.85f - U * 0.15f + S * 0.30f)
                                         : (-F * 0.90f - U * 0.20f + S * 0.25f);
                    var q = Quaternion.FromToRotation(b.rotation * a.eje[i], obj.normalized);
                    b.rotation = Quaternion.Slerp(Quaternion.identity, q, k) * b.rotation;
                }
            }
            if (flap > 0.001f)
                a.b[0].rotation = Quaternion.AngleAxis(Mathf.Sin(tiempo * 2f * Mathf.PI * 7f) * 45f * flap * a.lado, F) * a.b[0].rotation;
        }

        static float Suave(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        // Golpe de pico: baja rápido, se queda un instante y sube.
        static float Golpe(float p, float ini, float fin)
        {
            if (p < ini || p > fin) return 0f;
            float x = (p - ini) / (fin - ini);
            if (x < 0.35f) return Suave(x / 0.35f);
            if (x < 0.5f) return 1f;
            return 1f - Suave((x - 0.5f) / 0.5f);
        }

        static float Ruido(int i)
        {
            float h = Mathf.Sin(i * 12.9898f + 1.7f) * 43758.5453f;
            return (h - Mathf.Floor(h)) * 2f - 1f;
        }

        // Mirada de pájaro: gira la cabeza de golpe hacia un punto y se queda quieta.
        static float Mirada(float t)
        {
            const float T = 1.8f;
            int i = Mathf.FloorToInt(t / T);
            float x = (t - i * T) / T;
            return Mathf.Lerp(Ruido(i), Ruido(i + 1), Suave(x / 0.12f));
        }

        static float Pulso(float t, float T)
        {
            float x = (t % T) / T;
            return x < 0.08f ? Mathf.Sin(Mathf.PI * x / 0.08f) : 0f;
        }
    }
}
