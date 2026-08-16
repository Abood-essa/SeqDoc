using System;

namespace Branching;

public sealed class FlowShapes
{
    public int IfElse(int input)
    {
        int value = 1;
        if (input > 0)
        {
            value = input + 1;
        }
        else
        {
            value = input - 1;
        }

        return value;
    }

    public int ShortCircuit(bool first, int input)
    {
        if (first && input > 2)
        {
            return input;
        }

        return 0;
    }

    public string SwitchShape(int input) => input switch
    {
        1 => "one",
        2 => "two",
        _ => "other",
    };

    public int WhileShape(int limit)
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

    public int ThrowShape(int input)
    {
        try
        {
            if (input < 0)
            {
                throw new InvalidOperationException("negative");
            }

            return input;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        finally
        {
            Console.WriteLine(input);
        }
    }

    public bool NonVoidTerminal(bool flag)
    {
        if (flag)
        {
            return true;
        }

        return false;
    }

    public int UncaughtThrow(int input)
    {
        if (input < 0)
        {
            throw new InvalidOperationException("negative");
        }

        return input;
    }

    public int WrongCatchType(int input)
    {
        try
        {
            throw new InvalidOperationException("x");
        }
        catch (ArgumentException)
        {
            return 1;
        }
    }

    public int CaughtByBaseType(int input)
    {
        try
        {
            throw new InvalidOperationException("x");
        }
        catch (Exception)
        {
            return 1;
        }
    }

    public int RethrowCaughtByOuter(int input)
    {
        try
        {
            try
            {
                throw new InvalidOperationException("x");
            }
            catch (InvalidOperationException)
            {
                throw;
            }
        }
        catch (InvalidOperationException)
        {
            return 2;
        }
    }

    public int MixedSwitchAndThrow(int input)
    {
        var text = input switch
        {
            1 => "one",
            _ => "other",
        };
        if (input < 0)
        {
            throw new InvalidOperationException(text);
        }

        return input;
    }

    public int GenericCaughtThrow<T>(T exception) where T : InvalidOperationException
    {
        try
        {
            throw exception;
        }
        catch (InvalidOperationException)
        {
            return 1;
        }
    }
}
