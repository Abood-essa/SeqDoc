namespace Fake.Web
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ApiControllerAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HttpGetAttribute : Attribute
    {
        public HttpGetAttribute(string template)
        {
            Template = template;
        }

        public string? Template { get; }
    }

    public class ControllerBase
    {
        public object Ok() => new();
    }
}

namespace AspNetCoreControllers
{
    // Lookalike shapes: foreign fully qualified attribute identities and a foreign ControllerBase
    // with an Ok helper. The model must never recognize these from simple names.
    [Fake.Web.ApiController]
    public sealed class FakeController
    {
        [Fake.Web.HttpGet("fake-action")]
        public string FakeAction() => "fake";
    }

    public sealed class FakeControllerBaseDerived : Fake.Web.ControllerBase
    {
        public object Get() => Ok();
    }
}
