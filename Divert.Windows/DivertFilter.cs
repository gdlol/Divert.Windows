using System.Text;
using System.Text.RegularExpressions;

namespace Divert.Windows;

internal record struct ReplaceParenthesesOperation(string Expression);

public partial class DivertFilter
{
    public string Clause { get; }

    public DivertFilter(string clause)
    {
        ArgumentNullException.ThrowIfNull(clause);

        Clause = clause;
    }

    public static implicit operator DivertFilter(string clause)
    {
        return new DivertFilter(clause);
    }

    private static string ReplaceParentheses(string expression)
    {
        var builder = new StringBuilder();
        int index = 0;
        while (index < expression.Length)
        {
            char c = expression[index];
            if (c == ')')
            {
                var operation = new ReplaceParenthesesOperation(expression);
                throw new InvalidOperationException(operation.ToString());
            }
            else if (c == '(')
            {
                int stack = 1;
                while (stack > 0)
                {
                    index += 1;
                    if (index >= expression.Length)
                    {
                        var operation = new ReplaceParenthesesOperation(expression);
                        throw new InvalidOperationException(operation.ToString());
                    }
                    if (expression[index] == '(')
                    {
                        stack += 1;
                    }
                    else if (expression[index] == ')')
                    {
                        stack -= 1;
                    }
                }
                builder.Append('(');
                builder.Append(')');
            }
            else
            {
                builder.Append(c);
            }
            index += 1;
        }
        return builder.ToString();
    }

    [GeneratedRegex(@"\s(or|\|\|)\s")]
    private static partial Regex OrPatternRegex();

    private static bool MatchOrPattern(string s) => OrPatternRegex().IsMatch(ReplaceParentheses(s));

    [GeneratedRegex(@"\s(and|&&)\s")]
    private static partial Regex AndPatternRegex();

    private static bool MatchAndPattern(string s) => AndPatternRegex().IsMatch(ReplaceParentheses(s));

    public static DivertFilter operator &(DivertFilter left, DivertFilter right)
    {
        string leftClause = left.Clause;
        string rightClause = right.Clause;
        if (MatchOrPattern(leftClause))
        {
            leftClause = $"({left})";
        }
        if (MatchOrPattern(rightClause))
        {
            rightClause = $"({right})";
        }
        string clause = $"{leftClause} and {rightClause}";
        return new DivertFilter(clause);
    }

    public static DivertFilter operator |(DivertFilter left, DivertFilter right)
    {
        string leftClause = left.Clause;
        string rightClause = right.Clause;
        if (MatchAndPattern(leftClause))
        {
            leftClause = $"({left})";
        }
        if (MatchAndPattern(rightClause))
        {
            rightClause = $"({right})";
        }
        string clause = $"{leftClause} or {rightClause}";
        return new DivertFilter(clause);
    }

    public override string ToString()
    {
        return Clause;
    }

    public override bool Equals(object? obj)
    {
        return obj switch
        {
            null => false,
            DivertFilter filter => Clause == filter.Clause,
            string s => Clause == s,
            _ => false,
        };
    }

    public override int GetHashCode() => Clause.GetHashCode();

    public class Field
    {
        private readonly string field;

        public Field(string field)
        {
            ArgumentNullException.ThrowIfNull(field);

            this.field = field;
        }

        public override bool Equals(object? obj)
        {
            return obj switch
            {
                null => false,
                Field filter => field == filter.field,
                string s => field == s,
                _ => false,
            };
        }

        public override int GetHashCode() => field.GetHashCode();

        public override string ToString() => field;

        public static implicit operator Field(string field)
        {
            return new Field(field);
        }

        public static implicit operator DivertFilter(Field field)
        {
            return new DivertFilter(field.ToString());
        }

        public static DivertFilter operator &(Field left, Field right)
        {
            DivertFilter value = left;
            return value & right;
        }

        public static DivertFilter operator |(Field left, Field right)
        {
            DivertFilter value = left;
            return value | right;
        }

        public static DivertFilter operator !(Field value)
        {
            string clause = $"not {value}";
            return new DivertFilter(clause);
        }

        public static DivertFilter operator ==(Field left, object right)
        {
            string clause = $"{left} = {right}";
            return new DivertFilter(clause);
        }

        public static DivertFilter operator !=(Field left, object right)
        {
            string clause = $"{left} != {right}";
            return new DivertFilter(clause);
        }

        public static DivertFilter operator <(Field left, object right)
        {
            string clause = $"{left} < {right}";
            return new DivertFilter(clause);
        }

        public static DivertFilter operator >(Field left, object right)
        {
            string clause = $"{left} > {right}";
            return new DivertFilter(clause);
        }

        public static DivertFilter operator <=(Field left, object right)
        {
            string clause = $"{left} <= {right}";
            return new DivertFilter(clause);
        }

        public static DivertFilter operator >=(Field left, object right)
        {
            string clause = $"{left} >= {right}";
            return new DivertFilter(clause);
        }
    }

    public static DivertFilter True { get; } = "true";

    public static DivertFilter False { get; } = "false";

    public static Field Zero { get; } = "zero";

    public static Field Timestamp { get; } = "timestamp";

    public static Field Event { get; } = "event";

    public static Field Outbound { get; } = "outbound";

    public static Field Inbound { get; } = "inbound";

    public static Field InterfaceIndex { get; } = "ifIdx";

    public static Field SubInterfaceIndex { get; } = "subIfIdx";

    public static Field Loopback { get; } = "loopback";

    public static Field Impostor { get; } = "impostor";

    public static Field Fragment { get; } = "fragment";

    public static Field EndpointId { get; } = "endpointId";

    public static Field ParentEndpointId { get; } = "parentEndpointId";

    public static Field ProcessId { get; } = "processId";

    public static Field Random8 { get; } = "random8";

    public static Field Random16 { get; } = "random16";

    public static Field Random32 { get; } = "random32";

    public static Field Layer { get; } = "layer";

    public static Field Priority { get; } = "priority";

    public static Field Packet(int i) => $"packet[{i}]";

    public static Field Packet16(int i) => $"packet16[{i}]";

    public static Field Packet32(int i) => $"packet32[{i}]";

    public static Field Length { get; } = "length";

    public static Field Ip { get; } = "ip";

    public static Field Ipv6 { get; } = "ipv6";

    public static Field ICMP { get; } = "icmp";

    // spell-checker:ignore icmpv6
    public static Field ICMPv6 { get; } = "icmpv6";

    public static Field TCP { get; } = "tcp";

    public static Field UDP { get; } = "udp";

    public static Field Protocol { get; } = "protocol";

    public static Field LocalAddress { get; } = "localAddr";

    public static Field LocalPort { get; } = "localPort";

    public static Field RemoteAddress { get; } = "remoteAddr";

    public static Field RemotePort { get; } = "remotePort";
}
