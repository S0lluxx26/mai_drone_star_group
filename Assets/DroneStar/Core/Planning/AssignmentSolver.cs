using System;
using System.Numerics;

namespace DroneStar.Core
{
    /// <summary>
    /// Optimal drone-to-slot assignment. Minimising the sum of squared travel distances (Hungarian
    /// algorithm, O(n³)) is the assignment half of CAPT (Turpin, Michael &amp; Kumar, 2014): combined with
    /// synchronised straight-line trajectories it keeps drones apart during transitions.
    /// </summary>
    public static class AssignmentSolver
    {
        /// <summary>
        /// Returns <c>target[i]</c>: the index in <paramref name="to"/> assigned to <c>from[i]</c>.
        /// Both arrays must have the same length.
        /// </summary>
        public static int[] Solve(Vector3[] from, Vector3[] to)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));
            if (from.Length != to.Length) throw new ArgumentException("Assignment needs equally sized sets.");
            int n = from.Length;
            var result = new int[n];
            if (n == 0) return result;

            var cost = new double[n * n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    cost[i * n + j] = Vector3.DistanceSquared(from[i], to[j]);
                }
            }
            return Solve(cost, n);
        }

        /// <summary>Minimum-cost perfect matching on a dense n × n row-major cost matrix.</summary>
        public static int[] Solve(double[] cost, int n)
        {
            if (cost == null) throw new ArgumentNullException(nameof(cost));
            if (cost.Length != n * n) throw new ArgumentException("Cost matrix must be n × n.");
            var result = new int[n];
            if (n == 0) return result;

            // Shortest augmenting path formulation with row/column potentials (1-based, column 0 is a sentinel).
            var u = new double[n + 1];
            var v = new double[n + 1];
            var p = new int[n + 1];
            var way = new int[n + 1];
            var minv = new double[n + 1];
            var used = new bool[n + 1];

            for (int i = 1; i <= n; i++)
            {
                p[0] = i;
                int j0 = 0;
                for (int j = 0; j <= n; j++)
                {
                    minv[j] = double.PositiveInfinity;
                    used[j] = false;
                }
                do
                {
                    used[j0] = true;
                    int i0 = p[j0];
                    int row = (i0 - 1) * n;
                    double ui0 = u[i0];
                    double delta = double.PositiveInfinity;
                    int j1 = 0;
                    for (int j = 1; j <= n; j++)
                    {
                        if (used[j]) continue;
                        double reduced = cost[row + j - 1] - ui0 - v[j];
                        if (reduced < minv[j])
                        {
                            minv[j] = reduced;
                            way[j] = j0;
                        }
                        if (minv[j] < delta)
                        {
                            delta = minv[j];
                            j1 = j;
                        }
                    }
                    if (j1 == 0) throw new InvalidOperationException("Assignment failed: non-finite costs.");
                    for (int j = 0; j <= n; j++)
                    {
                        if (used[j])
                        {
                            u[p[j]] += delta;
                            v[j] -= delta;
                        }
                        else
                        {
                            minv[j] -= delta;
                        }
                    }
                    j0 = j1;
                } while (p[j0] != 0);

                do
                {
                    int j1 = way[j0];
                    p[j0] = p[j1];
                    j0 = j1;
                } while (j0 != 0);
            }

            for (int j = 1; j <= n; j++)
            {
                if (p[j] != 0) result[p[j] - 1] = j - 1;
            }
            return result;
        }

        public static double TotalCost(Vector3[] from, Vector3[] to, int[] assignment)
        {
            double total = 0;
            for (int i = 0; i < assignment.Length; i++) total += Vector3.DistanceSquared(from[i], to[assignment[i]]);
            return total;
        }
    }
}
