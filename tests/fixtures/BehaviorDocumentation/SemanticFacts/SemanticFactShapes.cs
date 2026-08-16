using System;

namespace SemanticFacts;

public static class SemanticFactShapes
{
    public static bool IsEqual(int left, int right)
    {
        return left == right;
    }

    public static bool IsBelow(int left, int right)
    {
        return left < right;
    }

    public static int Sum(int left, int right)
    {
        return left + right;
    }

    public static string Describe(int id, string name)
    {
        return $"{id}:{name}";
    }

    public static string CallWithReorderedArguments()
    {
        return Describe(name: "alpha", id: 7);
    }

    public static int ComputeValue(int input)
    {
        return input;
    }

    public static void Notify(string message)
    {
        Console.WriteLine(message);
        return;
    }
}
