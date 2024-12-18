﻿using Intersect.Enums;
using System;

namespace Intersect.Utilities
{

    public static partial class MathHelper
    {
        public static decimal Clamp(decimal value, decimal minimum, decimal maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }

        public static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }

        public static sbyte Clamp(sbyte value, sbyte minimum, sbyte maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }

        public static short Clamp(short value, short minimum, short maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }

        public static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }

        public static long Clamp(long value, long minimum, long maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }

        public static byte Clamp(byte value, byte minimum, byte maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }

        public static ushort Clamp(ushort value, ushort minimum, ushort maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }

        public static uint Clamp(uint value, uint minimum, uint maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }

        public static ulong Clamp(ulong value, ulong minimum, ulong maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }

    public static partial class MathHelper
    {
        public static T[,] RotateArray90CW<T>(int N, T[,] array)
        {
            // Consider all
            // squares one by one
            for (int x = 0; x < N / 2; x++)
            {
                // Consider elements
                // in group of 4 in
                // current square
                for (int y = x; y < N - x - 1; y++)
                {
                    // store current cell
                    // in temp variable
                    T temp = array[x, y];

                    // move values from
                    // right to top
                    array[x, y] = array[y, N - 1 - x];

                    // move values from
                    // bottom to right
                    array[y, N - 1 - x]
                        = array[N - 1 - x, N - 1 - y];

                    // move values from
                    // left to bottom
                    array[N - 1 - x, N - 1 - y]
                        = array[N - 1 - y, x];

                    // assign temp to left
                    array[N - 1 - y, x] = temp;
                }
            }

            return array;
        }

        public static void SwapInts(ref int a, ref int b)
        {
            a = a + b;
            b = a - b;
            a = a - b;
        }

        public static double DCos(double val)
        {
            return Math.Cos(val * (Math.PI / 180.0));
        }

        public static double DSin(double val)
        {
            return Math.Sin(val * (Math.PI / 180.0));
        }

        public static double DArcTan(double val)
        {
            return Math.Atan(val * (Math.PI / 180.0));
        }

        public static int RoundNearestMultiple(int value, int factor)
        {
            return (int)Math.Round(
                (value / (double)factor),
                MidpointRounding.AwayFromZero
            ) * factor;
        }

        public static uint GCD(uint a, uint b)
        {
            while (a != 0 && b != 0)
            {
                if (a > b)
                    a %= b;
                else
                    b %= a;
            }

            return a | b;
        }

        public static double CalculateDistanceToPoint(float selfX, float selfY, float otherX, float otherY)
        {
            var a = Math.Pow(otherX - selfX, 2);
            var b = Math.Pow(otherY - selfY, 2);

            return Math.Sqrt(a + b);
        }

        /// <summary>
        /// Returns true if you meet a random chance
        /// </summary>
        /// <param name="percentChance">A percent chance value, i.e 17.5 == 17.5%, 100.0 == 100%</param>
        /// <returns></returns>
        public static bool LuckRoll(float percentChance)
        {
            var randomChance = Randomization.Next(1, 100001);
            return randomChance < percentChance * 1000;
        }

        public static float GetRotationAngleFromDir(ProjectileDirections dir)
        {
            switch (dir)
            {
                case ProjectileDirections.Up:
                    return 0f;

                case ProjectileDirections.Down:
                    return 180f;

                case ProjectileDirections.Left:
                    return 270f;

                case ProjectileDirections.Right:
                    return 90f;

                case ProjectileDirections.UpLeft:
                    return 315f;

                case ProjectileDirections.UpRight:
                    return 45f;

                case ProjectileDirections.DownLeft:
                    return 225f;

                case ProjectileDirections.DownRight:
                    return 135f;

                default:
                    return 0f;
            }
        }

        public static double GetRotationAngleFromDirRad(ProjectileDirections dir)
        {
            return GetRotationAngleFromDir(dir) * (Math.PI / 180);
        }

        public static double CalculateRotatedWidth(float originalWidth, float originalHeight, float angleRadians)
        {
            return Math.Abs(originalWidth * Math.Cos(angleRadians)) +
                   Math.Abs(originalHeight * Math.Sin(angleRadians));
        }

        public static double CalculateRotatedHeight(float originalWidth, float originalHeight, float angleRadians)
        {
            return Math.Abs(originalWidth * Math.Sin(angleRadians)) +
                   Math.Abs(originalHeight * Math.Cos(angleRadians));
        }
    }

}
