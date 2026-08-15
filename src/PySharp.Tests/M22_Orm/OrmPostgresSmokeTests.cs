// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharp.Tests.M6_Stdlib;
using PySharpLib;

namespace PySharp.Tests.M22_Orm;

/// <summary>
/// The real end-to-end round trip ORM_PLAN.md Phase 3 set out to verify: real, unmodified
/// SQLAlchemy 2.0.51 driven through this project's own real `psycopg2` shim (SQL_PLAN.md Phase 2)
/// against a real Postgres server — `declarative_base()`, a mapped class, `create_all()`/
/// `drop_all()` (real DDL, including a real `has_table()` reflection round trip),
/// `Session.add()`/`.commit()` (a full real INSERT flush through SQLAlchemy 2.0's own
/// `insertmanyvalues` sentinel/batching machinery), and `session.execute(select(...))`/
/// `session.get()`. The pure-Python `pg8000` dialect originally planned for this was abandoned
/// after tracing it into a real module-system gap (`types.ModuleType(...)` doesn't yet construct
/// this interpreter's native module representation); driving the already-verified `psycopg2` shim
/// through SQLAlchemy's dialect layer instead got all the way to a real, general interpreter bug:
/// `SomeClass.__hash__` (unbound, class-level access) always hashed the class where the lookup
/// happened instead of whatever it was actually called on — silently breaking the real `__hash__ =
/// Operators.__hash__` idiom every `sqlalchemy.sql.operators.ColumnOperators` subclass (including
/// `Column`) relies on, which in turn corrupted `insertmanyvalues`' own sentinel-column filtering
/// (surfacing five frames away as a `ZeroDivisionError`). See ORM_PLAN.md Phase 3 for the full list
/// of real gaps found and fixed getting here — including a genuine `threading.Condition`
/// concurrency bug and a general zero-arg `super()` fix, neither Postgres-specific. Needs a real
/// reachable Postgres server — see <see cref="PostgresLiveFixture"/>; skips (not fails) on a
/// machine with no `PGHOST` set.
/// </summary>
public class OrmPostgresSmokeTests : IClassFixture<SqlAlchemyInstallFixture>, IClassFixture<PostgresLiveFixture>
{
    private readonly SqlAlchemyInstallFixture _sqlalchemy;
    private readonly PostgresLiveFixture _postgres;

    public OrmPostgresSmokeTests(SqlAlchemyInstallFixture sqlalchemy, PostgresLiveFixture postgres)
    {
        _sqlalchemy = sqlalchemy;
        _postgres = postgres;
    }

    [SkippableFact]
    public void A_mapped_class_round_trips_through_create_all_insert_and_select_against_real_postgres()
    {
        Skip.IfNot(_postgres.Available, "No Postgres server reachable (PGHOST not set or unreachable)");

        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_sqlalchemy.SitePackages);

        engine.Run("""
            import os
            from sqlalchemy import create_engine, Column, Integer, String, select
            from sqlalchemy.orm import declarative_base, Session

            Base = declarative_base()

            class User(Base):
                __tablename__ = "pysharp_orm_smoke_users"
                id = Column(Integer, primary_key=True)
                name = Column(String)
                email = Column(String)

            url = (
                f"postgresql+psycopg2://{os.environ['PGUSER']}:{os.environ['PGPASSWORD']}"
                f"@{os.environ['PGHOST']}:{os.environ.get('PGPORT', '5432')}"
                f"/{os.environ.get('PGDATABASE', 'postgres')}?sslmode=require"
            )
            engine = create_engine(url)

            Base.metadata.drop_all(engine)
            Base.metadata.create_all(engine)

            with Session(engine) as session:
                session.add(User(name="Ada", email="ada@example.com"))
                session.add(User(name="Bob", email="bob@example.com"))
                session.commit()

                users = session.execute(select(User).order_by(User.name)).scalars().all()
                for u in users:
                    print(u.id, u.name, u.email)

                print(session.get(User, 1).name)

            Base.metadata.drop_all(engine)
            """);
        Assert.Equal("1 Ada ada@example.com\n2 Bob bob@example.com\nAda\n", writer.ToString());
    }
}
