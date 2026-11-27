using System.Collections.Generic;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>Chapter one's antiques (GDD), each placed once on a generated map. Same set and numbers as UE5, in m.</summary>
    public static class DMValuableCatalog
    {
        public static List<DMValuableSpec> MakeChapterOne()
        {
            // UE sizes and speeds were in cm and cm/s.
            DMValuableSpec Item(string name, DMShape shape, Vector3 sizeCm, float mass, float crackCmPerS, int value, Color tint, float rough = 0.6f, float metal = 0f)
            {
                return new DMValuableSpec
                {
                    Name = name,
                    Shape = shape,
                    // UE (depth X, width Y, height Z) to Unity (width X, height Y, depth Z).
                    Size = new Vector3(sizeCm.y, sizeCm.z, sizeCm.x) * 0.01f,
                    MassKg = mass,
                    CrackSpeed = crackCmPerS * 0.01f,
                    Value = value,
                    Tint = tint,
                    Roughness = rough,
                    Metallic = metal
                };
            }

            Color gold = new Color(0.95f, 0.75f, 0.3f);
            Color glass = new Color(0.7f, 0.85f, 1f);
            Color red = new Color(0.8f, 0.1f, 0.12f);
            Color wood = new Color(0.45f, 0.28f, 0.15f);
            Color brass = new Color(0.75f, 0.55f, 0.25f);

            return new List<DMValuableSpec>
            {
                // Small (S): one hand, light, mostly fragile
                Item("크리스탈 쿠페 잔", DMShape.Sphere, new Vector3(25f, 25f, 25f), 0.5f, 220f, 1200, glass, 0.05f),
                Item("도자기 찻잔", DMShape.Cube, new Vector3(22f, 22f, 15f), 0.6f, 260f, 600, new Color(0.95f, 0.85f, 0.9f)),
                Item("토끼 머리 열쇠", DMShape.Cube, new Vector3(10f, 35f, 8f), 0.4f, 600f, 800, new Color(0.8f, 0.8f, 0.85f)),
                Item("회중시계", DMShape.Cylinder, new Vector3(18f, 18f, 6f), 0.5f, 380f, 700, brass, 0.25f, 1f),
                Item("향수병", DMShape.Cylinder, new Vector3(14f, 14f, 25f), 0.4f, 200f, 900, new Color(1f, 0.6f, 0.75f), 0.05f),
                Item("금박 달걀", DMShape.Sphere, new Vector3(20f, 20f, 26f), 0.8f, 350f, 1500, gold, 0.2f, 1f),
                // Medium (M): one player, heavy-ish
                Item("황동 오일 램프", DMShape.Cylinder, new Vector3(35f, 35f, 70f), 6f, 330f, 1400, brass, 0.25f, 1f),
                Item("붉은 하트 성물함", DMShape.Sphere, new Vector3(50f, 45f, 55f), 8f, 300f, 2500, red, 0.08f),
                Item("유리 랜턴", DMShape.Cube, new Vector3(40f, 40f, 70f), 7f, 300f, 1800, glass, 0.05f),
                Item("도자기 광대 인형", DMShape.Cube, new Vector3(35f, 30f, 50f), 5f, 280f, 1100, new Color(0.95f, 0.9f, 0.7f)),
                Item("별자리 지구본", DMShape.Sphere, new Vector3(55f, 55f, 55f), 9f, 450f, 1000, new Color(0.25f, 0.35f, 0.6f)),
                // Large (L): two players
                Item("회전목마 말", DMShape.Cube, new Vector3(120f, 40f, 90f), 32f, 400f, 3500, new Color(0.95f, 0.9f, 0.95f)),
                Item("대형 오르골", DMShape.Cube, new Vector3(100f, 70f, 60f), 35f, 300f, 4000, wood),
                Item("축음기", DMShape.Cylinder, new Vector3(80f, 80f, 90f), 30f, 400f, 3000, new Color(0.5f, 0.35f, 0.2f)),
                Item("금박 액자 그림", DMShape.Cube, new Vector3(10f, 140f, 110f), 25f, 380f, 2800, gold, 0.3f, 1f),
            };
        }
    }
}
