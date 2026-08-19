namespace InvocationArgumentExtraction;

public static class Shapes
{
    public static void Complete() => Describe(escaped: "line\n\"quote", token: "null", name: "alpha", id: 7);

    public static void OmittedIntermediateOptional() => Describe(id: 8, escaped: "tail");

    public static void NullAndSensitive() => Describe(9, null, "null", "AKIA" + "TEST000000000000");

    private static void Describe(int id, string? name = null, string? token = null, string? escaped = null)
    {
    }
}
