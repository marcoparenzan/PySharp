// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M4_Functions;

public class ClassTests
{
    [Fact]
    public void Class_with_init_and_methods()
    {
        string src = """
            class Point:
                def __init__(self, x, y):
                    self.x = x
                    self.y = y
                def dist2(self):
                    return self.x ** 2 + self.y ** 2
            p = Point(3, 4)
            print(p.x, p.y, p.dist2())
            """;
        Assert.Equal("3 4 25\n", Py.Run(src));
    }

    [Fact]
    public void Class_attributes_shared()
    {
        string src = """
            class C:
                count = 0
                def bump(self):
                    C.count += 1
            a = C()
            b = C()
            a.bump()
            b.bump()
            print(C.count)
            """;
        Assert.Equal("2\n", Py.Run(src));
    }

    [Fact]
    public void Inheritance_and_override()
    {
        string src = """
            class Animal:
                def speak(self):
                    return 'generic'
                def describe(self):
                    return 'I say ' + self.speak()
            class Dog(Animal):
                def speak(self):
                    return 'woof'
            print(Dog().describe())
            print(Animal().describe())
            """;
        Assert.Equal("I say woof\nI say generic\n", Py.Run(src));
    }

    [Fact]
    public void Super_calls_parent_init()
    {
        string src = """
            class Base:
                def __init__(self, name):
                    self.name = name
            class Child(Base):
                def __init__(self, name, age):
                    super().__init__(name)
                    self.age = age
            c = Child('bob', 5)
            print(c.name, c.age)
            """;
        Assert.Equal("bob 5\n", Py.Run(src));
    }

    [Fact]
    public void Multiple_inheritance_mro()
    {
        string src = """
            class A:
                def who(self):
                    return 'A'
            class B(A):
                pass
            class C(A):
                def who(self):
                    return 'C'
            class D(B, C):
                pass
            print(D().who())
            """;
        Assert.Equal("C\n", Py.Run(src)); // MRO C3: D, B, C, A
    }

    [Fact]
    public void Dunder_repr_str_eq()
    {
        string src = """
            class V:
                def __init__(self, n):
                    self.n = n
                def __repr__(self):
                    return 'V(' + str(self.n) + ')'
                def __eq__(self, other):
                    return isinstance(other, V) and self.n == other.n
            print(V(1))
            print(V(1) == V(1), V(1) == V(2))
            print([V(9)])
            """;
        Assert.Equal("V(1)\nTrue False\n[V(9)]\n", Py.Run(src));
    }

    [Fact]
    public void Operator_overloading()
    {
        string src = """
            class Vec:
                def __init__(self, x, y):
                    self.x = x
                    self.y = y
                def __add__(self, other):
                    return Vec(self.x + other.x, self.y + other.y)
                def __repr__(self):
                    return 'Vec(%d, %d)' % (self.x, self.y)
                def __len__(self):
                    return 2
                def __getitem__(self, i):
                    return (self.x, self.y)[i]
            v = Vec(1, 2) + Vec(10, 20)
            print(v, len(v), v[0], v[1])
            """;
        Assert.Equal("Vec(11, 22) 2 11 22\n", Py.Run(src));
    }

    [Fact]
    public void Property_getter_and_setter()
    {
        string src = """
            class Temp:
                def __init__(self):
                    self._c = 0
                @property
                def celsius(self):
                    return self._c
                @celsius.setter
                def celsius(self, v):
                    self._c = v
                @property
                def fahrenheit(self):
                    return self._c * 9 / 5 + 32
            t = Temp()
            t.celsius = 100
            print(t.celsius, t.fahrenheit)
            """;
        Assert.Equal("100 212.0\n", Py.Run(src));
    }

    [Fact]
    public void Static_and_class_methods()
    {
        string src = """
            class M:
                tag = 'M!'
                @staticmethod
                def add(a, b):
                    return a + b
                @classmethod
                def get_tag(cls):
                    return cls.tag
            print(M.add(1, 2))
            print(M.get_tag())
            print(M().add(3, 4))
            print(M().get_tag())
            """;
        Assert.Equal("3\nM!\n7\nM!\n", Py.Run(src));
    }

    [Fact]
    public void Callable_instances()
    {
        string src = """
            class Adder:
                def __init__(self, n):
                    self.n = n
                def __call__(self, x):
                    return x + self.n
            add5 = Adder(5)
            print(add5(10))
            """;
        Assert.Equal("15\n", Py.Run(src));
    }

    [Fact]
    public void Getattr_fallback()
    {
        string src = """
            class Proxy:
                def __getattr__(self, name):
                    return 'missing:' + name
            p = Proxy()
            print(p.anything)
            """;
        Assert.Equal("missing:anything\n", Py.Run(src));
    }

    [Fact]
    public void Isinstance_with_user_classes()
    {
        string src = """
            class A:
                pass
            class B(A):
                pass
            b = B()
            print(isinstance(b, B), isinstance(b, A), isinstance(b, (int, A)))
            print(issubclass(B, A), issubclass(A, B))
            """;
        Assert.Equal("True True True\nTrue False\n", Py.Run(src));
    }

    [Fact]
    public void Context_manager_protocol()
    {
        string src = """
            class CM:
                def __enter__(self):
                    print('enter')
                    return 42
                def __exit__(self, t, v, tb):
                    print('exit')
                    return False
            with CM() as x:
                print(x)
            """;
        Assert.Equal("enter\n42\nexit\n", Py.Run(src));
    }

    [Fact]
    public void Iterator_protocol()
    {
        string src = """
            class Countdown:
                def __init__(self, n):
                    self.n = n
                def __iter__(self):
                    return self
                def __next__(self):
                    if self.n <= 0:
                        raise StopIteration
                    self.n -= 1
                    return self.n + 1
            print(list(Countdown(3)))
            """;
        Assert.Equal("[3, 2, 1]\n", Py.Run(src));
    }

    /// <summary>Real object.__eq__/__ne__/__hash__/__repr__/__str__ default dunders — previously
    /// only reachable via hardcoded C# fallback branches in PyOps.Repr/Str/RichEquals (same output
    /// for normal repr()/str()/== use, so this is a transparent refactor there), but direct/unbound
    /// access (`object.__eq__`, `SomeClass.__eq__` when never overridden) raised AttributeError.
    /// Found via starlette's real `cls.__eq__ is object.__eq__`-style idiom, reachable from `import
    /// starlette`. See FASTAPI_PLAN.md Phase 3.</summary>
    [Fact]
    public void Object_default_dunders_are_real_and_directly_accessible()
        => Assert.Equal(
            "True\nFalse\nNotImplemented\nTrue\nTrue\nTrue",
            Py.Run("""
                x = object()
                y = object()
                print(object.__eq__(x, x))
                print(object.__eq__(x, x) is False)
                print(object.__eq__(x, y))
                print(object.__hash__(x) == object.__hash__(x))
                print(str(x) == repr(x))
                print(repr(x).startswith("<object object at"))
                """).TrimEnd('\n'));

    [Fact]
    public void Class_that_never_overrides_eq_finds_the_real_default_via_inheritance()
        => Assert.Equal("True\n", Py.Run("""
            class Base(object):
                pass
            print(Base.__eq__ is object.__eq__)
            """));
}
