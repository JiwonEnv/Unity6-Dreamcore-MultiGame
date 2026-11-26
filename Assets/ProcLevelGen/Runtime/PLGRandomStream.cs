using System;

namespace ProcLevelGen
{
    /// <summary>
    /// Bit-exact port of UE5's FRandomStream (Math/RandomStream.h), so a seed builds the same layout as the UE5 version.
    /// Only the calls the generator uses are ported.
    /// </summary>
    public sealed class PLGRandomStream
    {
        int initialSeed;
        uint seed;

        public PLGRandomStream(int inSeed)
        {
            Initialize(inSeed);
        }

        public int InitialSeed => initialSeed;

        public void Initialize(int inSeed)
        {
            initialSeed = inSeed;
            seed = unchecked((uint)inSeed);
        }

        public void Reset()
        {
            seed = unchecked((uint)initialSeed);
        }

        /// <summary>Random float in [0, 1).</summary>
        public float GetFraction()
        {
            MutateSeed();
            // Exponent of 1.0 with the top 23 bits of the seed as mantissa: [1, 2), then shifted to [0, 1).
            float result = BitConverter.Int32BitsToSingle(unchecked((int)(0x3F800000u | (seed >> 9))));
            return result - 1.0f;
        }

        public float FRand() => GetFraction();

        public uint GetUnsignedInt()
        {
            MutateSeed();
            return seed;
        }

        /// <summary>Random int in [0, a); 0 when a is not positive.</summary>
        public int RandHelper(int a)
        {
            // The (float) cast keeps the product at float precision, as in C++.
            return a > 0 ? (int)(float)(GetFraction() * (float)a) : 0;
        }

        /// <summary>Random int in [min, max].</summary>
        public int RandRange(int min, int max)
        {
            int range = max - min + 1;
            return min + RandHelper(range);
        }

        public bool RandBool() => RandHelper(2) == 1;

        void MutateSeed()
        {
            unchecked
            {
                seed = seed * 196314165u + 907633515u;
            }
        }
    }

    /// <summary>UE5 hash helpers the generator relies on for derived seeds and layout hashes.</summary>
    public static class PLGHash
    {
        /// <summary>UE5 HashCombineFast (Templates/TypeHash.h).</summary>
        public static uint CombineFast(uint a, uint b)
        {
            unchecked
            {
                return a ^ (b + 0x9e3779b9u + (a << 6) + (a >> 2));
            }
        }

        /// <summary>UE5 GetTypeHash of a 64-bit integer: low word plus 23 times the high word.</summary>
        public static uint TypeHash64(long value)
        {
            unchecked
            {
                ulong v = (ulong)value;
                return (uint)v + (uint)(v >> 32) * 23u;
            }
        }
    }
}
