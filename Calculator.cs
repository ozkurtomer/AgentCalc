namespace AgentCalc;

public static class Calculator
{
    public static double Topla(double a, double b) => a + b;

    public static double Cikar(double a, double b) => a - b;

    public static double Carp(double a, double b) => a * b;

    public static double Bol(double a, double b)
    {
        if (b == 0)
            throw new InvalidOperationException("Sıfıra bölme yapılamaz.");
        return a / b;
    }
}