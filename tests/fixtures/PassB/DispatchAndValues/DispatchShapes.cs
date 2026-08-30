using System;
using System.Threading.Tasks;

namespace DispatchAndValues;

public interface IPaymentProcessor
{
    string Process(int amount);
}

public abstract class PaymentProcessorBase : IPaymentProcessor
{
    public abstract string Process(int amount);
}

public sealed class CardPaymentProcessor : PaymentProcessorBase
{
    public override string Process(int amount) => amount > 0 ? "card" : "empty";
}

public sealed class CashPaymentProcessor : PaymentProcessorBase
{
    public override string Process(int amount) => $"cash:{amount}";
}

public sealed class ExplicitPaymentProcessor : IPaymentProcessor
{
    string IPaymentProcessor.Process(int amount) => $"explicit:{amount}";
}

public interface IDefaultProcessor
{
    string ProcessDefault(int amount) => "default";

    string ProcessAbstract(int amount);
}

public sealed class UsesDefaultProcessor : IDefaultProcessor
{
    public string ProcessAbstract(int amount) => amount.ToString();
}

public static class DispatchShapes
{
    public static string ProcessThroughInterface(IPaymentProcessor processor, int amount)
    {
        if (processor is null)
        {
            throw new ArgumentNullException(nameof(processor));
        }

        return processor.Process(amount);
    }

    public static string ProcessConcrete(CardPaymentProcessor processor, int amount)
    {
        return processor.Process(amount);
    }

    public static string ExplicitInterfaceShape(ExplicitPaymentProcessor processor, int amount)
    {
        return ((IPaymentProcessor)processor).Process(amount);
    }

    public static string DefaultInterfaceShape(IDefaultProcessor processor, int amount)
    {
        return processor.ProcessDefault(amount);
    }

    public static string DelegateShape(int amount)
    {
        Func<int, string> callback = value => value.ToString();
        return callback(amount);
    }

    public static string ObjectCreationShape(int amount)
    {
        var processor = new CardPaymentProcessor();
        return processor.Process(amount);
    }

    public static string DynamicShape(int amount)
    {
        dynamic value = amount;
        string text = value.ToString();
        return text;
    }

    public static int StaticShape(int amount)
    {
        return Math.Max(amount, 1);
    }

    public static int BclConstructorShape(int amount)
    {
        var list = new System.Collections.Generic.List<int>();
        list.Add(amount);
        return list.Count;
    }

    public static int EventShape(int amount)
    {
        var holder = new EventHolder();
        holder.Raised += (_, _) => { };
        holder.Raised -= (_, _) => { };
        holder.Raise();
        return amount;
    }

    public static async Task<int> AsyncShape(Task<int> source)
    {
        var value = await source;
        return value + 1;
    }

    public static int DoWhileShape(int limit)
    {
        int total = 0;
        int index = 0;
        do
        {
            total += index;
            index++;
        }
        while (index < limit);
        return total;
    }

    public static int ForLoopShape(int limit)
    {
        int total = 0;
        for (int index = 0; index < limit; index++)
        {
            total += index;
        }

        return total;
    }

    public static int ForEachShape(int[] values)
    {
        int total = 0;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }

    public static int NestedForEachShape(int[][] values)
    {
        var total = 0;
        foreach (var group in values)
        {
            foreach (var value in group)
            {
                total += value;
            }
        }

        return total;
    }

    public static int WhileLoopShape(int limit)
    {
        int total = 0;
        int index = 0;
        while (index < limit)
        {
            total += index;
            index++;
        }

        return total;
    }

    public static int SequentialLoopShape(int firstLimit, int secondLimit)
    {
        var total = 0;
        for (var first = 0; first < firstLimit; first++)
        {
            total += first;
        }

        var second = 0;
        while (second < secondLimit)
        {
            total += second;
            second++;
        }

        return total;
    }

    public static int NestedLoopShape(int outerLimit, int innerLimit)
    {
        var total = 0;
        for (var outer = 0; outer < outerLimit; outer++)
        {
            var inner = 0;
            while (inner < innerLimit)
            {
                total += outer + inner;
                inner++;
            }
        }

        return total;
    }

    public static int MultipleLatchLoopShape(int limit)
    {
        var total = 0;
        var index = 0;
        while (index < limit)
        {
            if ((index & 1) == 0)
            {
                index++;
                continue;
            }

            total += index++;
        }

        return total;
    }

    public static int FinallyBoundaryShape(int value)
    {
        try
        {
            return value;
        }
        finally
        {
            _ = value.ToString();
        }
    }

    public static int LocalFunctionNestedLoopShape(int limit)
    {
        int LocalLoop(int value)
        {
            while (value-- > 0)
            {
            }

            return value;
        }

        return LocalLoop(limit);
    }

    public static int AnonymousFunctionNestedLoopShape(int limit)
    {
        Func<int, int> loop = value =>
        {
            while (value-- > 0)
            {
            }

            return value;
        };
        return loop(limit);
    }

    public static int CatchToLoopShape(int limit)
    {
        while (limit-- > 0)
        {
            try
            {
                _ = int.Parse(limit.ToString());
            }
            catch (FormatException)
            {
                continue;
            }
        }

        return limit;
    }

    public static int NestedTryCatchLoopShape(int limit)
    {
        while (limit-- > 0)
        {
            try
            {
                try
                {
                    _ = int.Parse(limit.ToString());
                }
                catch (FormatException)
                {
                    continue;
                }
            }
            catch (Exception)
            {
                continue;
            }
        }

        return limit;
    }

    public static int UnreachableLoopShape(int limit)
    {
        return 0;
#pragma warning disable CS0162
        while (limit-- > 0)
        {
            limit--;
        }
#pragma warning restore CS0162
    }

    public static int ReflectionShape(int amount)
    {
        var method = typeof(string).GetMethod("get_Length", Type.EmptyTypes);
        var result = method?.Invoke(amount.ToString(), null);
        return result is int length ? length : 0;
    }

    public static int GeneratedBodyShape(int amount)
    {
        var holder = new AutoHolder { Value = amount };
        return holder.Value;
    }

    public static int VirtualClassShape(BaseProcessor processor)
    {
        return processor.Compute();
    }
}

public sealed class EventHolder
{
    public event EventHandler? Raised;

    public void Raise() => Raised?.Invoke(this, EventArgs.Empty);
}

public sealed class AutoHolder
{
    public int Value { get; set; }
}

public class BaseProcessor
{
    public virtual int Compute() => 0;
}

public sealed class AddProcessor : BaseProcessor
{
    public override int Compute() => 1;
}

public sealed class MultiplyProcessor : BaseProcessor
{
    public override int Compute() => 2;
}

public sealed class HidingProcessor : BaseProcessor
{
    public new int Compute() => 3;
}
