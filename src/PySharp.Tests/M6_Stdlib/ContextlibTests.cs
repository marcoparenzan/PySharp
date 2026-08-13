// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>contextlib.contextmanager and contextlib.suppress (see AIOMQTT_PLAN.md Phase 1).</summary>
public class ContextlibTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Contextmanager_normal_path_runs_before_and_after_yield()
        => Assert.Equal("enter,body:42,exit", Run("""
            import contextlib

            log = []

            @contextlib.contextmanager
            def cm():
                log.append('enter')
                yield 42
                log.append('exit')

            with cm() as v:
                log.append('body:%d' % v)

            print(','.join(log))
            """));

    [Fact]
    public void Contextmanager_finally_runs_and_exception_propagates()
        => Assert.Equal("cleanup,caught:boom", Run("""
            import contextlib

            log = []

            @contextlib.contextmanager
            def cm():
                try:
                    yield
                finally:
                    log.append('cleanup')

            try:
                with cm():
                    raise ValueError('boom')
            except ValueError as e:
                log.append('caught:%s' % e)

            print(','.join(log))
            """));

    [Fact]
    public void Contextmanager_can_suppress_the_with_body_exception()
        => Assert.Equal("done", Run("""
            import contextlib

            @contextlib.contextmanager
            def swallow():
                try:
                    yield
                except ValueError:
                    pass

            with swallow():
                raise ValueError('boom')

            print('done')
            """));

    [Fact]
    public void Contextmanager_works_as_a_bound_method()
        => Assert.Equal("enter:x,body,exit:x", Run("""
            import contextlib

            class C:
                def __init__(self):
                    self.log = []

                @contextlib.contextmanager
                def cm(self, tag):
                    self.log.append('enter:' + tag)
                    yield
                    self.log.append('exit:' + tag)

            c = C()
            with c.cm('x'):
                c.log.append('body')
            print(','.join(c.log))
            """));

    [Fact]
    public void Suppress_swallows_listed_exception()
        => Assert.Equal("ok", Run("""
            import contextlib

            with contextlib.suppress(KeyError):
                raise KeyError('x')
            print('ok')
            """));

    [Fact]
    public void Suppress_does_not_swallow_unlisted_exception()
        => Assert.Equal("propagated", Run("""
            import contextlib

            try:
                with contextlib.suppress(KeyError):
                    raise ValueError('x')
            except ValueError:
                print('propagated')
            """));

    // contextlib.nullcontext: found while probing real sqlalchemy (see ORM_PLAN.md Phase 0), used
    // as a real placeholder context manager where no real lock/transaction is needed.
    [Fact]
    public void Nullcontext_enter_returns_the_given_value_and_exit_does_nothing()
        => Assert.Equal("42\ndone", Run("""
            import contextlib

            with contextlib.nullcontext(42) as v:
                print(v)
            print('done')
            """));

    [Fact]
    public void Nullcontext_defaults_to_None_and_propagates_an_exception_from_the_body()
        => Assert.Equal("None\ncaught", Run("""
            import contextlib

            with contextlib.nullcontext() as v:
                print(v)
            try:
                with contextlib.nullcontext():
                    raise ValueError('boom')
            except ValueError:
                print('caught')
            """));

}
