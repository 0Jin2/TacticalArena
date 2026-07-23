// WARNING: Do not modify! Generated file.

namespace UnityEngine.Purchasing.Security {
    public class GooglePlayTangle
    {
        private static byte[] data = System.Convert.FromBase64String("OSRlT9eagMmOLJbIpxUUrE0DIxwbyEioMWyza89+6EzmcNSIL8Z5ay2uoK+fLa6lrS2urq96S0BaZGMEh6hcKB/U7t9losxErUE+4b2tR1qxQuCZHpsYbNQIeezp9ipGHzd1tPfysVaDiJr4P+IOlEXLxRy7Z46FuSq2Nvncpri2tc1Odwl93DXENe2SOsLopgj6kmhmLdWow6tG9BCenr+r52bBe3D0D20SW2aPJDQlTjMfNuYNuA214Y0PQKnofJ8VBIgd9S/oGalYYio8T03oL+DuVL4Pl3rAqkV/hJVJHgvoHWNPDBrOlLA1t22gny2ujZ+iqaaFKecpWKKurq6qr6x5qm/3WgEGZT8HnClKrv3iP3rsZ2RKcV7dB3C15K2srq+u");
        private static int[] order = new int[] { 7,10,10,13,13,12,13,8,10,9,12,13,12,13,14 };
        private static int key = 175;

        public static readonly bool IsPopulated = true;

        public static byte[] Data() {
        	if (IsPopulated == false)
        		return null;
            return Obfuscator.DeObfuscate(data, order, key);
        }
    }
}
