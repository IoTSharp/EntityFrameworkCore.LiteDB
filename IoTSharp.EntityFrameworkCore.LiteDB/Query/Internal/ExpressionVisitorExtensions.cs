using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace System.Linq.Expressions;

[DebuggerStepThrough]
internal static class ExpressionVisitorExtensions
{
	/// <summary>
	///     Dispatches the list of expressions to one of the more specialized visit methods in this class.
	/// </summary>
	/// <param name="visitor">The expression visitor.</param>
	/// <param name="nodes">The expressions to visit.</param>
	/// <returns>
	///     The modified expression list, if any of the elements were modified; otherwise, returns the original expression list.
	/// </returns>
	public static IReadOnlyList<Expression> Visit(this ExpressionVisitor visitor, IReadOnlyList<Expression> nodes)
	{
		Expression[] array = null;
		int i = 0;
		for (int count = nodes.Count; i < count; i++)
		{
			Expression expression = visitor.Visit(nodes[i]);
			if (array != null)
			{
				array[i] = expression;
			}
			else if (expression != nodes[i])
			{
				array = new Expression[count];
				for (int j = 0; j < i; j++)
				{
					array[j] = nodes[j];
				}
				array[i] = expression;
			}
		}
		IReadOnlyList<Expression> readOnlyList = array;
		return readOnlyList ?? nodes;
	}

	/// <summary>
	///     Visits an expression, casting the result back to the original expression type.
	/// </summary>
	/// <typeparam name="T">The type of the expression.</typeparam>
	/// <param name="visitor">The expression visitor.</param>
	/// <param name="nodes">The expression to visit.</param>
	/// <param name="callerName">The name of the calling method; used to report a better error message.</param>
	/// <returns>
	///     The modified expression, if it or any subexpression was modified; otherwise, returns the original expression.
	/// </returns>
	/// <exception cref="T:System.InvalidOperationException">The visit method for this node returned a different type.</exception>
	public static IReadOnlyList<T> VisitAndConvert<T>(this ExpressionVisitor visitor, IReadOnlyList<T> nodes, [CallerMemberName] string callerName = null) where T : Expression
	{
		T[] array = null;
		int i = 0;
		for (int count = nodes.Count; i < count; i++)
		{
			if (!(visitor.Visit(nodes[i]) is T val))
			{
				throw new InvalidOperationException(CoreStrings.MustRewriteToSameNode((object)callerName, (object)typeof(T).Name));
			}
			if (array != null)
			{
				array[i] = val;
			}
			else if (val != nodes[i])
			{
				array = new T[count];
				for (int j = 0; j < i; j++)
				{
					array[j] = nodes[j];
				}
				array[i] = val;
			}
		}
		IReadOnlyList<T> readOnlyList = array;
		return readOnlyList ?? nodes;
	}

	/// <summary>
	///     Visits all nodes in the collection using a specified element visitor.
	/// </summary>
	/// <typeparam name="T">The type of the nodes.</typeparam>
	/// <param name="visitor">The expression visitor.</param>
	/// <param name="nodes">The nodes to visit.</param>
	/// <param name="elementVisitor">
	///     A delegate that visits a single element,
	///     optionally replacing it with a new element.
	/// </param>
	/// <returns>
	///     The modified node list, if any of the elements were modified;
	///     otherwise, returns the original node list.
	/// </returns>
	public static IReadOnlyList<T> Visit<T>(this ExpressionVisitor visitor, IReadOnlyList<T> nodes, Func<T, T> elementVisitor)
	{
		T[] array = null;
		int i = 0;
		for (int count = nodes.Count; i < count; i++)
		{
			T val = elementVisitor(nodes[i]);
			if (array != null)
			{
				array[i] = val;
			}
			else if ((object)val != (object)nodes[i])
			{
				array = new T[count];
				for (int j = 0; j < i; j++)
				{
					array[j] = nodes[j];
				}
				array[i] = val;
			}
		}
		IReadOnlyList<T> readOnlyList = array;
		return readOnlyList ?? nodes;
	}
}
