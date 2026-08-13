// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;

namespace PySharp.Tests.M22_Orm;

/// <summary>
/// The real end-to-end round trip ORM_PLAN.md Phase 1 set out to verify: a mapped class, real
/// `create_all()` DDL, `Session.add()`/`commit()` (a full real INSERT flush, including the
/// `insertmanyvalues`/RETURNING machinery), and `session.execute(select(...))`/`session.get()`
/// against a real SQLite database — all running real sqlalchemy 2.0.51, not a stub. Getting here
/// took ~30 real, general interpreter fixes (see ORM_PLAN.md for the full list), none of them
/// sqlalchemy-specific: real `class Foo(dict/list/set/str/int): ...` subclassing, `__slots__`
/// descriptor semantics, PEP 487 `__init_subclass__`, the general descriptor protocol (including on
/// plain functions themselves — `func.__get__`), real name mangling, metaclass `__init__` dispatch,
/// metaclass-level binary/comparison operators, `instance.__dict__ = ...` whole-namespace
/// replacement, and a real `abc.ABCMeta` base for the `type` pseudo-class hierarchy so
/// ABCMeta-derived custom metaclasses (a common real pattern — pydantic's `ModelMetaclass` uses the
/// exact same shape) are recognized as real metaclasses.
/// </summary>
public class OrmSmokeTests : IClassFixture<SqlAlchemyInstallFixture>
{
    private readonly SqlAlchemyInstallFixture _fixture;

    public OrmSmokeTests(SqlAlchemyInstallFixture fixture) => _fixture = fixture;

    [Fact]
    public void Import_sqlalchemy_succeeds()
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);

        engine.Run("import sqlalchemy\nprint(sqlalchemy.__version__)");
        Assert.Equal("2.0.51\n", writer.ToString());
    }

    [Fact]
    public void A_mapped_class_round_trips_through_create_all_insert_and_select()
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);

        engine.Run("""
            from sqlalchemy import create_engine, Column, Integer, String, select
            from sqlalchemy.orm import declarative_base, Session

            Base = declarative_base()

            class User(Base):
                __tablename__ = "users"
                id = Column(Integer, primary_key=True)
                name = Column(String)
                email = Column(String)

            engine = create_engine("sqlite:///:memory:")
            Base.metadata.create_all(engine)

            with Session(engine) as session:
                session.add(User(name="Ada", email="ada@example.com"))
                session.add(User(name="Bob", email="bob@example.com"))
                session.commit()

                users = session.execute(select(User).order_by(User.name)).scalars().all()
                for u in users:
                    print(u.id, u.name, u.email)

                print(session.get(User, 1).name)
            """);
        Assert.Equal("1 Ada ada@example.com\n2 Bob bob@example.com\nAda\n", writer.ToString());
    }
}
