public class NaturalCubicSpline
{
    private readonly double[] x;
    private readonly double[] y;
    private readonly double[] secondDerivatives;

    public NaturalCubicSpline(double[] x, double[] y)
    {
        if (x.Length != y.Length || x.Length < 2)
            throw new ArgumentException("Invalid input data for spline.");

        this.x = x;
        this.y = y;
        secondDerivatives = ComputeSecondDerivatives();
    }

    public double Evaluate(double xValue)
    {
        int i = Array.BinarySearch(x, xValue);
        if (i >= 0) return y[i];

        i = ~i - 1;
        if (i < 0) i = 0;
        if (i >= x.Length - 1) i = x.Length - 2;

        double h = x[i + 1] - x[i];
        double a = (x[i + 1] - xValue) / h;
        double b = (xValue - x[i]) / h;
        return a * y[i] + b * y[i + 1] + ((a * a * a - a) * secondDerivatives[i] +
                (b * b * b - b) * secondDerivatives[i + 1]) * (h * h) / 6.0;
    }

    public NaturalCubicSpline Bump(double bumpAmount)
    {
        double[] bumpedY = y.Select(val => val + bumpAmount).ToArray();
        return new NaturalCubicSpline(x, bumpedY);
    }

    private double[] ComputeSecondDerivatives()
    {
        int n = x.Length;
        double[] u = new double[n];
        double[] d2y = new double[n];

        for (int i = 1; i < n - 1; i++)
        {
            double h1 = x[i] - x[i - 1];
            double h2 = x[i + 1] - x[i];
            double alpha = 3.0 * ((y[i + 1] - y[i]) / h2 - (y[i] - y[i - 1]) / h1);
            double l = h1 / (h1 + h2);
            double mu = h2 / (h1 + h2);
            double z = 2.0;

            u[i] = alpha - l * u[i - 1] / z;
            z = 2.0 - mu * l / z;
            d2y[i] = (u[i] - mu * d2y[i - 1]) / z;
        }

        return d2y;
    }
}
