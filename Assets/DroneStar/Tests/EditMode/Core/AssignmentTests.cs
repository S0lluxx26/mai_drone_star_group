using System;
using System.Collections.Generic;
using System.Numerics;
using DroneStar.Core;
using NUnit.Framework;

namespace DroneStar.Tests
{
    public class AssignmentTests
    {
        [Test]
        public void SolvesAKnownMatrix()
        {
            double[] cost =
            {
                4, 1, 3,
                2, 0, 5,
                3, 2, 2,
            };
            int[] a = AssignmentSolver.Solve(cost, 3);
            Assert.That(a, Is.EqualTo(new[] { 1, 0, 2 }));
        }

        [Test]
        public void MatchesBruteForceOnRandomSets([Values(1, 2, 3, 4, 5, 6, 7)] int n)
        {
            var rng = new Random(1234 + n);
            for (int trial = 0; trial < 20; trial++)
            {
                Vector3[] from = RandomPoints(rng, n);
                Vector3[] to = RandomPoints(rng, n);
                int[] a = AssignmentSolver.Solve(from, to);
                double best = double.MaxValue;
                foreach (int[] perm in Permutations(n)) best = Math.Min(best, AssignmentSolver.TotalCost(from, to, perm));
                Assert.That(AssignmentSolver.TotalCost(from, to, a), Is.EqualTo(best).Within(1e-6));
            }
        }

        [Test]
        public void ReturnsAPermutationForLargeSets()
        {
            var rng = new Random(7);
            Vector3[] from = RandomPoints(rng, 400);
            Vector3[] to = RandomPoints(rng, 400);
            int[] a = AssignmentSolver.Solve(from, to);
            var seen = new bool[400];
            foreach (int j in a)
            {
                Assert.That(seen[j], Is.False);
                seen[j] = true;
            }
        }

        [Test]
        public void IdenticalSetsMapToThemselves()
        {
            var rng = new Random(3);
            Vector3[] pts = RandomPoints(rng, 50);
            int[] a = AssignmentSolver.Solve(pts, pts);
            for (int i = 0; i < 50; i++) Assert.That(a[i], Is.EqualTo(i));
        }

        [Test]
        public void RejectsMismatchedSizes()
        {
            Assert.Throws<ArgumentException>(() => AssignmentSolver.Solve(new Vector3[2], new Vector3[3]));
            Assert.That(AssignmentSolver.Solve(new Vector3[0], new Vector3[0]).Length, Is.EqualTo(0));
        }

        static Vector3[] RandomPoints(Random rng, int n)
        {
            var p = new Vector3[n];
            for (int i = 0; i < n; i++) p[i] = new Vector3((float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 20f);
            return p;
        }

        static IEnumerable<int[]> Permutations(int n)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = i;
            return Permute(a, 0);
        }

        static IEnumerable<int[]> Permute(int[] a, int k)
        {
            if (k == a.Length)
            {
                yield return (int[])a.Clone();
                yield break;
            }
            for (int i = k; i < a.Length; i++)
            {
                (a[k], a[i]) = (a[i], a[k]);
                foreach (int[] p in Permute(a, k + 1)) yield return p;
                (a[k], a[i]) = (a[i], a[k]);
            }
        }
    }
}
