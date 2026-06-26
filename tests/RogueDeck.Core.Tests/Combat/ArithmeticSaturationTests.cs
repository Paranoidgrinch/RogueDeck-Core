using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Arithmetic on int expressions saturates to the int range instead of throwing OverflowException on
// pathological inputs (e.g. a read of an int.MaxValue pool times a large operand).
public class ArithmeticSaturationTests
{
    private static ConstantExpression<CardPlayContext> C(int value) => new(value);

    // Constants ignore the execution context, so it can be null for these pure-arithmetic checks.
    private static int Eval(ICombatExpression<CardPlayContext, int> expression) => expression.Evaluate(null!, null!);

    [Fact]
    public void Add_Subtract_SaturateInsteadOfOverflowing()
    {
        Assert.Equal(int.MaxValue, Eval(new AddExpression<CardPlayContext>(C(int.MaxValue), C(10))));
        Assert.Equal(int.MinValue, Eval(new SubtractExpression<CardPlayContext>(C(int.MinValue), C(10))));
    }

    [Fact]
    public void Multiply_Saturates()
    {
        Assert.Equal(int.MaxValue, Eval(new MultiplyExpression<CardPlayContext>(C(int.MaxValue), C(2))));
        Assert.Equal(int.MinValue, Eval(new MultiplyExpression<CardPlayContext>(C(int.MaxValue), C(-2))));
    }

    [Fact]
    public void Negate_Abs_Divide_HandleIntMinValue()
    {
        Assert.Equal(int.MaxValue, Eval(new NegateExpression<CardPlayContext>(C(int.MinValue))));
        Assert.Equal(int.MaxValue, Eval(new AbsExpression<CardPlayContext>(C(int.MinValue))));
        Assert.Equal(int.MaxValue, Eval(new DivideExpression<CardPlayContext>(C(int.MinValue), C(-1))));
    }

    [Fact]
    public void NormalArithmeticIsUnchanged()
    {
        Assert.Equal(7, Eval(new AddExpression<CardPlayContext>(C(3), C(4))));
        Assert.Equal(12, Eval(new MultiplyExpression<CardPlayContext>(C(3), C(4))));
        Assert.Equal(2, Eval(new DivideExpression<CardPlayContext>(C(7), C(3))));
    }
}
