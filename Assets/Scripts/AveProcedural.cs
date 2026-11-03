// Visor 3D — movimientos de ave hechos por código (sin clip): reposo, picoteo y salto.
// Trabaja sobre el esqueleto de Forja Animación (torso, pecho, cuello, cabeza, cola_raiz, ala_L/R_1..3).
// · Parte siempre de la pose de unión de la piel (bindposes), no de la pose por defecto del FBX
//   (que es un fotograma de un clip y dejaba la cabeza y las alas torcidas).
// · Las alas se pliegan como un ave posada: cada tramo se orienta (dirección + cara de las plumas)
//   a lo largo del cuerpo, pegado al costado y hacia el lomo.
// · Endereza la cabeza (Meshy a veces la entrega mirando de lado), también encima de los clips.
// Ejes medidos de la malla (alas = eje lateral, cabeza = adelante): no depende de cómo se exportó el FBX.
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
        [Tooltip("Dirección del pico en el espacio local del hueso cabeza (la calcula Forja3DPreparar).")]
        public Vector3 picoLocal = Vector3.zero;
        [Range(0f, 1f)] public float enderezarCabeza = 1f;

        /// <summary>Altura extra del cuerpo (salto). La aplica el visor al pivote del modelo.</summary>
        public float Elevacion { get; private set; }

        class Ala
        {
            public Transform[] b = new Transform[3];
            public Vector3[] eje = new Vector3[3];    // dirección del tramo, local al hueso
            public Vector3[] normal = new Vector3[3]; // cara superior de las plumas, local al hueso
            public float lado = 1f;
        }

        Transform[] todos; Quaternion[] rot0; Vector3[] pos0;
        Transform torso, cuello, cabeza, cola, cola1;
        Ala[] alas;
        Vector3 fL, uL, rL; // adelante, arriba y derecha del ave en el espacio local de la raíz

        void Awake()
        {
            PoseDeUnion();
            todos = GetComponentsInChildren<Transform>(true);
            rot0 = new Quaternion[todos.Length]; pos0 = new Vector3[todos.Length];
            for (int i = 0; i < todos.Length; i++) { rot0[i] = todos[i].localRotation; pos0[i] = todos[i].localPosition; }

            torso = Buscar("torso"); cuello = Buscar("cuello"); cabeza = Buscar("cabeza");
            cola = Buscar("cola_raiz"); cola1 = Buscar("cola1");
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
                if (picoLocal.sqrMagnitude > 1e-6f) haciaCabeza += Vector3.ProjectOnPlane(cabeza.rotation * picoLocal, up);
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

        // Coloca cada hueso en la pose con la que se pegó la piel: hueso = malla · bindpose⁻¹.
        void PoseDeUnion()
        {
            var smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (!smr || !smr.sharedMesh) return;
            var huesos = smr.bones; var bp = smr.sharedMesh.bindposes;
            if (huesos == null || bp == null || huesos.Length != bp.Length) return;
            var malla = smr.transform.localToWorldMatrix;
            // padres antes que hijos
            var orden = new System.Collections.Generic.List<int>();
            for (int i = 0; i < huesos.Length; i++) if (huesos[i]) orden.Add(i);
            orden.Sort((x, y) => Profundidad(huesos[x]).CompareTo(Profundidad(huesos[y])));
            foreach (int i in orden)
            {
                var m = malla * bp[i].inverse;
                huesos[i].SetPositionAndRotation(m.GetColumn(3), m.rotation);
            }
        }

        static int Profundidad(Transform t) { int d = 0; while (t.parent) { d++; t = t.parent; } return d; }

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
                if (dir.sqrMagnitude < 1e-12f) dir = b.rotation * Vector3.up;
                dir.Normalize();
                var n = Vector3.ProjectOnPlane(Vector3.up, dir);
                if (n.sqrMagnitude < 1e-8f) n = Vector3.ProjectOnPlane(Vector3.forward, dir);
                a.eje[i] = Quaternion.Inverse(b.rotation) * dir;
                a.normal[i] = Quaternion.Inverse(b.rotation) * n.normalized;
            }
            return a;
        }

        static Vector3? Punta(Ala a)
        {
            for (int i = 2; i >= 0; i--) if (a.b[i]) return a.b[i].childCount > 0 ? a.b[i].GetChild(0).position : a.b[i].position;
            return null;
        }

        /// <summary>Vuelve a la pose de unión (antes de pasar el control al Animator).</summary>
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
            Vector3 F = transform.TransformDirection(fL), U = transform.TransformDirection(uL), R = transform.TransformDirection(rL);
            if (modo == Modo.Nada)
            {   // encima del clip del Animator: solo enderezar la cabeza
                Elevacion = 0f;
                Enderezar(F, U);
                return;
            }
            tiempo += Time.deltaTime * velocidad;
            Restaurar();

            float pliegue = 1f, cabeceo = 0f, giroCabeza = 0f, inclinacion = 0f, colaAng = 0f, aleteo = 0f;
            float elev = 0f;
            const float PI = Mathf.PI;

            switch (modo)
            {
                case Modo.Reposo:
                {
                    float t = tiempo;
                    giroCabeza = Mirada(t) * 30f;
                    cabeceo = Mathf.Sin(t * 1.3f) * 3f + Mathf.Sin(t * 0.37f) * 3f;
                    inclinacion = Mathf.Sin(t * 2f * PI * 0.3f) * 1.2f; // respiración
                    colaAng = Mathf.Sin(t * 0.9f) * 3f + Pulso(t, 3.7f) * 14f;
                    break;
                }
                case Modo.Picoteo:
                {
                    const float T = 1.5f;
                    float p = (tiempo % T) / T;
                    float k = Mathf.Max(Golpe(p, 0.08f, 0.32f), 0.8f * Golpe(p, 0.40f, 0.62f));
                    cabeceo = 50f * k;
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
            Vector3 F2 = qT * F;
            // eje del cuerpo (de los hombros a la cola) y lomo, ya con el torso inclinado
            Vector3 hombros = Vector3.zero; int nh = 0;
            foreach (var a in alas) if (a.b[0]) { hombros += a.b[0].position; nh++; }
            hombros = nh > 0 ? hombros / nh : (torso ? torso.position : transform.position);
            Vector3 D = (cola1 ? cola1.position : (cola ? cola.position : hombros - F2)) - hombros;
            D = D.sqrMagnitude > 1e-10f ? D.normalized : -F2;
            Vector3 B = Vector3.ProjectOnPlane(-F2, D);
            B = B.sqrMagnitude > 1e-8f ? B.normalized : U;
            foreach (var a in alas) Plegar(a, pliegue, F2, R, D, B, aleteo);
            Enderezar(F2, U);
            if (cuello) cuello.rotation = Quaternion.AngleAxis(giroCabeza * 0.4f, U) * Quaternion.AngleAxis(cabeceo * 0.6f, R) * cuello.rotation;
            if (cabeza) cabeza.rotation = Quaternion.AngleAxis(giroCabeza * 0.6f, U) * Quaternion.AngleAxis(cabeceo * 0.4f, R) * cabeza.rotation;
            if (cola) cola.rotation = Quaternion.AngleAxis(colaAng, R) * cola.rotation;
        }

        // Gira cuello y cabeza alrededor de la vertical para que el pico apunte hacia adelante.
        void Enderezar(Vector3 F, Vector3 U)
        {
            if (enderezarCabeza <= 0f || !cabeza || picoLocal.sqrMagnitude < 1e-6f) return;
            var d = Vector3.ProjectOnPlane(cabeza.rotation * picoLocal, U);
            var f = Vector3.ProjectOnPlane(F, U);
            if (d.sqrMagnitude < 1e-8f || f.sqrMagnitude < 1e-8f) return;
            float ang = Mathf.Clamp(Vector3.SignedAngle(d, f, U), -70f, 70f) * enderezarCabeza;
            if (cuello) cuello.rotation = Quaternion.AngleAxis(ang * 0.35f, U) * cuello.rotation;
            cabeza.rotation = Quaternion.AngleAxis(cuello ? ang * 0.65f : ang, U) * cabeza.rotation;
        }

        // Plegado de ave posada: brazo hacia la cola, antebrazo de vuelta, mano hacia la cola,
        // todo pegado al costado y hacia el lomo, con la cara de las plumas mirando hacia afuera.
        void Plegar(Ala a, float k, Vector3 F, Vector3 R, Vector3 D, Vector3 B, float flap)
        {
            if (!a.b[0]) return;
            Vector3 S = R * a.lado;
            if (k > 0.001f)
            {
                for (int i = 0; i < 3; i++)
                {
                    var b = a.b[i]; if (!b) continue;
                    Vector3 obj = i == 0 ? (D + S * 0.05f + B * 0.3f)
                                : i == 1 ? (-D - S * 0.10f + B * 0.3f)
                                         : (D - S * 0.10f + B * 0.3f);
                    var actual = Quaternion.LookRotation(b.rotation * a.eje[i], b.rotation * a.normal[i]);
                    var meta = Quaternion.LookRotation(obj.normalized, S);
                    var q = meta * Quaternion.Inverse(actual);
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
