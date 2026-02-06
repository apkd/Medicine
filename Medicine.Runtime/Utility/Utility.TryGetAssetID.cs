#nullable enable
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using static System.Runtime.CompilerServices.MethodImplOptions;

namespace Medicine.Internal
{
    public static partial class Utility
    {
        public static bool TryGetAssetID(UnityEngine.Object obj, out uint4 id)
        {
#if UNITY_EDITOR
            if (!UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long _))
            {
                id = default;
                return false;
            }

            return TryParseGuid128(guid, out id);

            [MethodImpl(AggressiveInlining)]
            static bool TryParseGuid128(string guid, out uint4 id)
            {
                if (guid is not { Length: 32 }
                    || !TryParse(guid, 00, out var a)
                    || !TryParse(guid, 08, out var b)
                    || !TryParse(guid, 16, out var c)
                    || !TryParse(guid, 24, out var d))
                {
                    id = default;
                    return false;
                }

                id = new(a, b, c, d);
                return true;
            }

            [MethodImpl(AggressiveInlining)]
            static bool TryParse(string guid, int start, out uint value)
            {
                value = 0;

                for (int i = 0; i < 8; i++)
                {
                    int nibble = Read(guid[start + i]);
                    if (nibble < 0)
                        return false;

                    value = (value << 4) | (uint)nibble;
                }

                return true;
            }

            [MethodImpl(AggressiveInlining)]
            static int Read(char c)
            {
                if ((uint)(c - '0') <= 9u)
                    return c - '0';

                if ((uint)(c - 'a') <= 5u)
                    return c - 'a' + 10;

                if ((uint)(c - 'A') <= 5u)
                    return c - 'A' + 10;

                return -1;
            }
#else
            id = default;
            return false;
#endif
        }
    }
}