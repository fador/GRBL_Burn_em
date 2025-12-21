using System;
using System.Collections.Generic;
using System.Linq;

namespace laser_gui_test.Tools
{
    public class MyMatrix
    {
        public double[,] Data;
        public int Rows;
        public int Cols;

        public MyMatrix(int rows, int cols)
        {
            Rows = rows;
            Cols = cols;
            Data = new double[rows, cols];
        }

        public MyMatrix(double[,] data)
        {
            Rows = data.GetLength(0);
            Cols = data.GetLength(1);
            Data = data;
        }

        public double this[int r, int c]
        {
            get => Data[r, c];
            set => Data[r, c] = value;
        }

        public MyMatrix Transpose()
        {
            var res = new MyMatrix(Cols, Rows);
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    res[c, r] = Data[r, c];
            return res;
        }

        public MyMatrix Multiply(MyMatrix b)
        {
            if (Cols != b.Rows) throw new Exception("Matrix dimension mismatch");
            var res = new MyMatrix(Rows, b.Cols);
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < b.Cols; c++)
                {
                    double sum = 0;
                    for (int k = 0; k < Cols; k++)
                        sum += Data[r, k] * b[k, c];
                    res[r, c] = sum;
                }
            return res;
        }
        
        public static MyMatrix operator *(MyMatrix a, MyMatrix b) => a.Multiply(b);
        public static MyMatrix operator *(double s, MyMatrix a)
        {
            var res = new MyMatrix(a.Rows, a.Cols);
            for(int r=0; r<a.Rows; r++)
                for(int c=0; c<a.Cols; c++)
                    res[r,c] = a[r,c] * s;
            return res;
        }

        public MyMatrix SubMatrix(int r, int c, int h, int w)
        {
             var res = new MyMatrix(h, w);
             for(int i=0; i<h; i++)
                for(int j=0; j<w; j++)
                    res[i,j] = Data[r+i, c+j];
             return res;
        }

        // 3x3 Inverse
        public MyMatrix Inverse()
        {
            if (Rows != 3 || Cols != 3) throw new Exception("Only 3x3 Inverse implemented");
            
            double det = Data[0, 0] * (Data[1, 1] * Data[2, 2] - Data[2, 1] * Data[1, 2]) -
                         Data[0, 1] * (Data[1, 0] * Data[2, 2] - Data[1, 2] * Data[2, 0]) +
                         Data[0, 2] * (Data[1, 0] * Data[2, 1] - Data[1, 1] * Data[2, 0]);

            double invDet = 1.0 / det;

            MyMatrix res = new MyMatrix(3, 3);
            res[0, 0] = (Data[1, 1] * Data[2, 2] - Data[2, 1] * Data[1, 2]) * invDet;
            res[0, 1] = (Data[0, 2] * Data[2, 1] - Data[0, 1] * Data[2, 2]) * invDet;
            res[0, 2] = (Data[0, 1] * Data[1, 2] - Data[0, 2] * Data[1, 1]) * invDet;
            res[1, 0] = (Data[1, 2] * Data[2, 0] - Data[1, 0] * Data[2, 2]) * invDet;
            res[1, 1] = (Data[0, 0] * Data[2, 2] - Data[0, 2] * Data[2, 0]) * invDet;
            res[1, 2] = (Data[1, 0] * Data[0, 2] - Data[0, 0] * Data[1, 2]) * invDet;
            res[2, 0] = (Data[1, 0] * Data[2, 1] - Data[2, 0] * Data[1, 1]) * invDet;
            res[2, 1] = (Data[2, 0] * Data[0, 1] - Data[0, 0] * Data[2, 1]) * invDet;
            res[2, 2] = (Data[0, 0] * Data[1, 1] - Data[1, 0] * Data[0, 1]) * invDet;

            return res;
        }
        
        public double[] Multiply(double[] v)
        {
            if (Cols != v.Length) throw new Exception("Dimension mismatch");
            double[] res = new double[Rows];
            for(int r=0; r<Rows; r++)
            {
                double sum = 0;
                for(int c=0; c<Cols; c++) sum += Data[r,c] * v[c];
                res[r] = sum;
            }
            return res;
        }
    }

    public static class DefaultAlgebra
    {
        public static double Dot(double[] a, double[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++) sum += a[i] * b[i];
            return sum;
        }

        public static double[] Subtract(double[] a, double[] b)
        {
            double[] res = new double[a.Length];
            for (int i = 0; i < a.Length; i++) res[i] = a[i] - b[i];
            return res;
        }
        
        public static double L2Norm(double[] a)
        {
            return Math.Sqrt(Dot(a, a));
        }

        public static double[] Scale(double s, double[] a)
        {
            return a.Select(x => x * s).ToArray();
        }

        public static double[] Cross(double[] a, double[] b)
        {
            return new double[] {
                a[1]*b[2] - a[2]*b[1],
                a[2]*b[0] - a[0]*b[2],
                a[0]*b[1] - a[1]*b[0]
            };
        }

        // Returns eigenvector corresponding to smallest eigenvalue of A^T A
        // Used for DLT (Homography)
        public static double[] SolveHomogeneous(MyMatrix A)
        {
            // We need V from A = U S V^T
            // V are eigenvectors of A^T A
            // We want the one for smallest eigenvalue.
            
            MyMatrix At = A.Transpose();
            MyMatrix AtA = At.Multiply(A); // Square, symmetric

            var eigen = JacobiEigen(AtA);
            
            // Find smallest eigenvalue
            int minIdx = 0;
            double minVal = eigen.values[0];
            for(int i=1; i<eigen.values.Length; i++)
            {
                if (eigen.values[i] < minVal)
                {
                    minVal = eigen.values[i];
                    minIdx = i;
                }
            }

            // Return that column
            double[] res = new double[eigen.vectors.Rows];
            for(int r=0; r<res.Length; r++) res[r] = eigen.vectors[r, minIdx];
            return res;
        }
        
        public class EigenResult { public double[] values = Array.Empty<double>(); public MyMatrix vectors = new MyMatrix(0,0); }

        public static EigenResult JacobiEigen(MyMatrix A, int maxIter = 100)
        {
            // Jacobi algorithm for symmetric matrices
            int n = A.Rows;
            MyMatrix V = new MyMatrix(n, n);
            for(int i=0; i<n; i++) V[i,i] = 1.0;
            
            MyMatrix D = new MyMatrix((double[,])A.Data.Clone());
            
            for(int iter=0; iter<maxIter; iter++)
            {
                // Find pivot
                double maxVal = 0.0;
                int p=0, q=0;
                for(int i=0; i<n-1; i++)
                {
                    for(int j=i+1; j<n; j++)
                    {
                        if (Math.Abs(D[i,j]) > maxVal)
                        {
                            maxVal = Math.Abs(D[i,j]);
                            p=i; q=j;
                        }
                    }
                }
                
                if (maxVal < 1e-12) break;
                
                double theta = 0.5 * Math.Atan2(2*D[p,q], D[p,p] - D[q,q]);
                double c = Math.Cos(theta);
                double s = Math.Sin(theta);
                
                // Update D
                // We only need to update rows/cols p and q, but full update is easier to implement safely first
                // Actually let's do safe full rotation: D' = J^T D J
                
                // Optimized update (standard Jacobi):
                double Dpp = D[p,p];
                double Dqq = D[q,q];
                double Dpq = D[p,q];
                
                D[p,p] = c*c*Dpp - 2*s*c*Dpq + s*s*Dqq;
                D[q,q] = s*s*Dpp + 2*s*c*Dpq + c*c*Dqq;
                D[p,q] = 0;
                D[q,p] = 0; // maintain symmetry
                
                for(int i=0; i<n; i++)
                {
                    if (i!=p && i!=q)
                    {
                        double Dip = D[i,p];
                        double Diq = D[i,q];
                        D[i,p] = c*Dip - s*Diq;
                        D[p,i] = D[i,p];
                        D[i,q] = s*Dip + c*Diq;
                        D[q,i] = D[i,q];
                    }
                }
                
                // Update V
                for(int i=0; i<n; i++)
                {
                    double Vip = V[i,p];
                    double Viq = V[i,q];
                    V[i,p] = c*Vip - s*Viq;
                    V[i,q] = s*Vip + c*Viq;
                }
            }
            
            double[] vals = new double[n];
            for(int i=0; i<n; i++) vals[i] = D[i,i];
            
            return new EigenResult { values = vals, vectors = V };
        }
        
        // Approximates SVD U*V^T for orthogonalization
        public static MyMatrix Orthogonalize3x3(MyMatrix M)
        {
            // To find Rotation R from M = U S V^T, R = U V^T
            // We solve via Eigen(M^T M) to get V and S
            // V are eigenvectors of M^T M
            // S^2 are eigenvalues
            
            var MtM = M.Transpose().Multiply(M);
            var eigen = JacobiEigen(MtM);
            
            // Singular values
            var V = eigen.vectors;
            var S = new double[3];
            for(int i=0; i<3; i++) S[i] = Math.Sqrt(Math.Max(0, eigen.values[i]));
            
            // U = M * V * S^-1
            var U = new MyMatrix(3, 3);
            
            // There's a catch: Jacobi doesn't sort eigenvalues.
            // But we iterate all.
            // Also need to handle near-zero singular values.
            
            // Construct U column by column: u_i = M * v_i / s_i
            for(int i=0; i<3; i++)
            {
                double s = S[i];
                if (s > 1e-9)
                {
                    for(int r=0; r<3; r++)
                    {
                        double sum = 0;
                        for(int k=0; k<3; k++) sum += M[r,k] * V[k,i];
                        U[r,i] = sum / s;
                    }
                }
                else
                {
                    // Degenerate case? Just keep 0 or handle nicely?
                    // For rotation matrices, this shouldn't happen usually.
                }
            }
            
            // R = U * V^T
            return U.Multiply(V.Transpose());
        }
    }
}
