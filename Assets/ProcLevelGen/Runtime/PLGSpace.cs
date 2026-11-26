using UnityEngine;

namespace ProcLevelGen
{
    /// <summary>
    /// Plan space (the module spec, same as UE5: cm, X forward, Y right, Z up) to Unity local space (m, Z forward,
    /// X right, Y up). Both are left-handed, so a plan yaw is the same angle as a Unity yaw.
    /// </summary>
    public static class PLGSpace
    {
        public const float CmToM = 0.01f;

        public static Vector3 ToUnity(Vector3 plan) => new Vector3(plan.y, plan.z, plan.x) * CmToM;

        public static Vector3 ToUnity(Vector2 plan, float z = 0f) => new Vector3(plan.y, z, plan.x) * CmToM;

        /// <summary>Box size in plan cm to a Unity scale in m.</summary>
        public static Vector3 SizeToUnity(Vector3 planSize) => new Vector3(planSize.y, planSize.z, planSize.x) * CmToM;

        public static Vector3 ToPlan(Vector3 unity) => new Vector3(unity.z, unity.x, unity.y) / CmToM;

        public static Quaternion Yaw(float degrees) => Quaternion.Euler(0f, degrees, 0f);
    }
}
