using AudiobookManager.Database.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Exercises the error classifiers that let a repository distinguish a transient SQLite failure
/// it should retry from one it must surface.
/// </summary>
[TestClass]
public class SqliteErrorsTests
{
    private static SqliteException Busy(int extendedCode) =>
        new("database is locked", /* errorCode */ 5, /* extendedErrorCode */ extendedCode);

    [TestMethod]
    public void IsBusyLocked_BusyPrimaryCode_ReturnsTrue()
    {
        Assert.IsTrue(SqliteErrors.IsBusyLocked(Busy(5)));
    }

    [TestMethod]
    public void IsBusyLocked_BusySnapshot_ReturnsTrue()
    {
        // SQLITE_BUSY_SNAPSHOT (261) is a distinct extended code under the same primary busy code.
        Assert.IsTrue(SqliteErrors.IsBusyLocked(Busy(261)));
    }

    [TestMethod]
    public void IsBusyLocked_Locked_ReturnsTrue()
    {
        Assert.IsTrue(SqliteErrors.IsBusyLocked(
            new SqliteException("database table is locked", /* errorCode */ 6, /* extendedErrorCode */ 6)));
    }

    [TestMethod]
    public void IsBusyLocked_UniqueViolation_ReturnsFalse()
    {
        Assert.IsFalse(SqliteErrors.IsBusyLocked(
            new SqliteException("UNIQUE constraint failed", /* errorCode */ 19, /* extendedErrorCode */ 2067)));
    }

    [TestMethod]
    public void IsBusyLocked_UnrelatedError_ReturnsFalse()
    {
        Assert.IsFalse(SqliteErrors.IsBusyLocked(new Exception("boom")));
    }

    [TestMethod]
    public void IsBusyLocked_WrappedInDbUpdateException_ReturnsTrue()
    {
        // SaveChanges succeeds as a DbUpdateException wrapping the store failure; the
        // repository's insert path must let this escape to its retry loop rather than swallow it.
        var wrapped = new DbUpdateException("save failed", Busy(5));
        Assert.IsTrue(SqliteErrors.IsBusyLocked(wrapped));
    }

    [TestMethod]
    public void IsUniqueViolation_WrappedBusyLocked_ReturnsFalse()
    {
        // A busy lock is not "another request inserted the row first". Classifying it as a unique
        // violation would make a transient lock read as a spent budget (the insert never committed,
        // so the follow-up update touches nothing and TryConsumeAsync reports false).
        var wrapped = new DbUpdateException("save failed", Busy(5));
        Assert.IsFalse(SqliteErrors.IsUniqueViolation(wrapped));
    }
}
