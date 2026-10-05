using Printman.Core.Models;

namespace Printman.Tests.Models;

[TestClass]
public sealed class IppModelTests
{
    // ---------------------------------------------------------------------
    // IppValue accessors
    // ---------------------------------------------------------------------

    [TestMethod]
    public void IppValue_AsInt_ReturnsOnlyForIntegers()
    {
        Assert.AreEqual(5, new IppValue(IppTag.Integer, 5).AsInt());
        Assert.AreEqual(5, new IppValue(IppTag.Enum, 5).AsInt());
        Assert.IsNull(new IppValue(IppTag.TextWithoutLanguage, "5").AsInt());
    }

    [TestMethod]
    public void IppValue_AsBool_ReturnsOnlyForBooleans()
    {
        Assert.AreEqual(true, new IppValue(IppTag.Boolean, true).AsBool());
        Assert.IsNull(new IppValue(IppTag.Integer, 1).AsBool());
    }

    [TestMethod]
    public void IppValue_AsString_ReturnsOnlyForStrings()
    {
        Assert.AreEqual("hello", new IppValue(IppTag.TextWithoutLanguage, "hello").AsString());
        Assert.IsNull(new IppValue(IppTag.Integer, 1).AsString());
    }

    [TestMethod]
    public void IppValue_AsCollection_ReturnsOnlyForCollections()
    {
        var collection = new IppCollection();
        Assert.AreSame(collection, new IppValue(IppTag.BegCollection, collection).AsCollection());
        Assert.IsNull(new IppValue(IppTag.Integer, 1).AsCollection());
    }

    [TestMethod]
    public void IppValue_AsRange_ReturnsOnlyForRanges()
    {
        var range = new IppValue(IppTag.RangeOfInteger, new IppRange(1, 3)).AsRange();

        Assert.IsNotNull(range);
        Assert.AreEqual(1, range.Value.Lower);
        Assert.AreEqual(3, range.Value.Upper);
        Assert.IsNull(new IppValue(IppTag.Integer, 1).AsRange());
    }

    // ---------------------------------------------------------------------
    // IppAttribute / IppAttributeList
    // ---------------------------------------------------------------------

    [TestMethod]
    public void IppAttribute_First_IsNullWhenEmpty()
    {
        var attribute = new IppAttribute("empty");

        Assert.AreEqual("empty", attribute.Name);
        Assert.IsNull(attribute.First);
    }

    [TestMethod]
    public void IppAttributeList_Get_IsCaseInsensitiveAndNullWhenMissing()
    {
        var list = new IppAttributeList();
        list.AddKeyword("Copies", "1");

        Assert.IsNotNull(list.Get("copies"));
        Assert.IsNotNull(list.Get("COPIES"));
        Assert.IsNull(list.Get("missing"));
    }

    [TestMethod]
    public void IppAttributeList_AddHelpers_RecordTagAndValues()
    {
        var list = new IppAttributeList();

        list.AddKeyword("keyword", "a", "b");
        list.AddKeywords("keywords", new[] { "x", "y" });
        list.AddInteger("integer", 1, 2);
        list.AddEnum("enum", 3);
        list.AddBoolean("boolean", true);
        list.AddText("text", "t");
        list.AddName("name", "n");
        list.AddUri("uri", "ipp://host");
        list.AddCharset("charset", "utf-8");
        list.AddLanguage("language", "en");
        list.AddMimeTypes("mime", new[] { "application/pdf" });
        list.AddResolution("resolution", new IppResolution(300, 600, 3));
        list.AddRange("range", 1, 5);
        list.AddCollection("collection", new IppCollection());
        list.AddNoValue("novalue");

        Assert.AreEqual(IppTag.Keyword, list.Get("keyword")!.First!.Tag);
        Assert.AreEqual(2, list.Get("keyword")!.Values.Count);
        Assert.AreEqual(IppTag.Keyword, list.Get("keywords")!.First!.Tag);
        Assert.AreEqual(2, list.Get("integer")!.Values.Count);
        Assert.AreEqual(IppTag.Enum, list.Get("enum")!.First!.Tag);
        Assert.AreEqual(true, list.Get("boolean")!.First!.AsBool());
        Assert.AreEqual(IppTag.TextWithoutLanguage, list.Get("text")!.First!.Tag);
        Assert.AreEqual(IppTag.NameWithoutLanguage, list.Get("name")!.First!.Tag);
        Assert.AreEqual(IppTag.Uri, list.Get("uri")!.First!.Tag);
        Assert.AreEqual(IppTag.Charset, list.Get("charset")!.First!.Tag);
        Assert.AreEqual(IppTag.NaturalLanguage, list.Get("language")!.First!.Tag);
        Assert.AreEqual(IppTag.MimeMediaType, list.Get("mime")!.First!.Tag);
        Assert.AreEqual(IppTag.Resolution, list.Get("resolution")!.First!.Tag);
        Assert.AreEqual(IppTag.RangeOfInteger, list.Get("range")!.First!.Tag);
        Assert.AreEqual(IppTag.BegCollection, list.Get("collection")!.First!.Tag);
        Assert.AreEqual(IppTag.NoValue, list.Get("novalue")!.First!.Tag);
        Assert.IsNull(list.Get("novalue")!.First!.Value);
    }

    [TestMethod]
    public void IppAttributeList_AddWithoutValues_AddsNoValue()
    {
        var list = new IppAttributeList();

        list.Add("empty", IppTag.Keyword);

        Assert.AreEqual(IppTag.NoValue, list.Get("empty")!.First!.Tag);
        Assert.IsNull(list.Get("empty")!.First!.Value);
    }

    [TestMethod]
    public void IppAttributeList_AddDateTime_UsesEncodedBytes()
    {
        var when = new DateTimeOffset(2024, 1, 2, 3, 4, 5, 600, TimeSpan.FromHours(-5));
        var list = new IppAttributeList();

        list.AddDateTime("date", when);

        var value = list.Get("date")!.First!;
        Assert.AreEqual(IppTag.DateTime, value.Tag);
        CollectionAssert.AreEqual(IppAttributeList.EncodeDateTime(when), (byte[])value.Value!);
    }

    [TestMethod]
    public void EncodeDateTime_NegativeOffset_EncodesSignAndComponents()
    {
        var when = new DateTimeOffset(2024, 1, 2, 3, 4, 5, 600, TimeSpan.FromHours(-5));

        var bytes = IppAttributeList.EncodeDateTime(when);

        Assert.AreEqual(11, bytes.Length);
        Assert.AreEqual((byte)(2024 >> 8), bytes[0]);
        Assert.AreEqual(unchecked((byte)2024), bytes[1]);
        Assert.AreEqual((byte)1, bytes[2]);
        Assert.AreEqual((byte)2, bytes[3]);
        Assert.AreEqual((byte)3, bytes[4]);
        Assert.AreEqual((byte)4, bytes[5]);
        Assert.AreEqual((byte)5, bytes[6]);
        Assert.AreEqual((byte)6, bytes[7]);
        Assert.AreEqual((byte)'-', bytes[8]);
        Assert.AreEqual((byte)5, bytes[9]);
        Assert.AreEqual((byte)0, bytes[10]);
    }

    [TestMethod]
    public void EncodeDateTime_PositiveOffset_EncodesSignAndComponents()
    {
        var when = new DateTimeOffset(2024, 6, 7, 8, 9, 10, 0, TimeSpan.FromMinutes(330));

        var bytes = IppAttributeList.EncodeDateTime(when);

        Assert.AreEqual((byte)'+', bytes[8]);
        Assert.AreEqual((byte)5, bytes[9]);
        Assert.AreEqual((byte)30, bytes[10]);
    }

    // ---------------------------------------------------------------------
    // IppMessage navigation
    // ---------------------------------------------------------------------

    [TestMethod]
    public void IppMessage_GroupAndFindAttribute_NavigateGroups()
    {
        var message = new IppMessage();

        Assert.IsNull(message.Group(IppTag.OperationAttributes));

        var operation = message.AddGroup(IppTag.OperationAttributes);
        operation.AddKeyword("operation-attr", "1");
        var job = message.AddGroup(IppTag.JobAttributes);
        job.AddKeyword("job-attr", "2");

        Assert.AreSame(operation, message.Group(IppTag.OperationAttributes));
        Assert.AreSame(job, message.Group(IppTag.JobAttributes));

        Assert.IsNotNull(message.FindRequestAttribute("operation-attr"));
        Assert.IsNotNull(message.FindRequestAttribute("job-attr"));
        Assert.IsNull(message.FindRequestAttribute("missing"));

        Assert.IsNotNull(message.FindJobAttribute("job-attr"));
        Assert.IsNotNull(message.FindJobAttribute("operation-attr"));
        Assert.IsNull(message.FindJobAttribute("missing"));
    }

    // ---------------------------------------------------------------------
    // IppJob state machine
    // ---------------------------------------------------------------------

    [TestMethod]
    public void IppJob_WithoutTicket_AwaitsDocumentAndIsPending()
    {
        var job = NewJob();

        Assert.IsTrue(job.AwaitingDocument);
        Assert.IsNull(job.Ticket);
        Assert.IsNull(job.DocumentFormat);
        Assert.IsNull(job.StartedAt);
        Assert.IsNull(job.CompletedAt);
        Assert.AreEqual(0, job.PagesPrinted);

        var state = job.GetState();
        Assert.AreEqual(IppJobState.Pending, state.State);
        Assert.AreEqual("job-incoming", state.Reason);
        Assert.AreEqual("Waiting for document data.", state.Message);
    }

    [TestMethod]
    public void IppJob_AttachedTicket_MapsPending()
    {
        var job = NewJob();
        var ticket = NewTicket();

        job.Attach(ticket, "application/pdf");

        Assert.IsFalse(job.AwaitingDocument);
        Assert.AreSame(ticket, job.Ticket);
        Assert.AreEqual("application/pdf", job.DocumentFormat);
        Assert.AreEqual(IppJobState.Pending, job.GetState().State);
        Assert.AreEqual("none", job.GetState().Reason);
    }

    [TestMethod]
    public void IppJob_AttachedTicket_MapsProcessingAndTracksProgress()
    {
        var job = NewJob();
        var ticket = NewTicket();
        job.Attach(ticket, "application/pdf");

        ticket.MarkProcessing();
        ticket.AddPagesPrinted(4);

        var state = job.GetState();
        Assert.AreEqual(IppJobState.Processing, state.State);
        Assert.AreEqual("job-printing", state.Reason);
        Assert.AreEqual("Printing.", state.Message);
        Assert.IsNotNull(job.StartedAt);
        Assert.AreEqual(4, job.PagesPrinted);
    }

    [TestMethod]
    public void IppJob_AttachedTicket_MapsCompletedAndCanceled()
    {
        var completed = NewJob();
        var completedTicket = NewTicket();
        completed.Attach(completedTicket, "application/pdf");
        completedTicket.Finish(PipelineJobState.Completed);

        var completedState = completed.GetState();
        Assert.AreEqual(IppJobState.Completed, completedState.State);
        Assert.AreEqual("job-completed-successfully", completedState.Reason);
        Assert.AreEqual("Sent to printer.", completedState.Message);
        Assert.IsNotNull(completed.CompletedAt);

        var canceled = NewJob();
        var canceledTicket = NewTicket();
        canceled.Attach(canceledTicket, "application/pdf");
        canceledTicket.Finish(PipelineJobState.Canceled);

        var canceledState = canceled.GetState();
        Assert.AreEqual(IppJobState.Canceled, canceledState.State);
        Assert.AreEqual("job-canceled-by-user", canceledState.Reason);
        Assert.AreEqual("Canceled.", canceledState.Message);
    }

    [TestMethod]
    public void IppJob_FailedTicket_MapsToAborted()
    {
        var job = NewJob();
        var ticket = NewTicket();
        job.Attach(ticket, "application/pdf");
        ticket.Finish(PipelineJobState.Failed, "no paper");

        var state = job.GetState();
        Assert.AreEqual(IppJobState.Aborted, state.State);
        Assert.AreEqual("aborted-by-system", state.Reason);
        Assert.AreEqual("no paper", state.Message);
    }

    [TestMethod]
    public void IppJob_FailedTicketWithoutMessage_UsesFallbackMessage()
    {
        var job = NewJob();
        var ticket = NewTicket();
        job.Attach(ticket, "application/pdf");
        ticket.Finish(PipelineJobState.Failed);

        Assert.AreEqual("Printing failed.", job.GetState().Message);
    }

    [TestMethod]
    public void IppJob_Terminate_OverridesStateAndIsIdempotent()
    {
        var job = NewJob();

        job.Terminate(IppJobState.Canceled, "job-canceled-by-user");

        Assert.IsFalse(job.AwaitingDocument);
        Assert.IsNotNull(job.CompletedAt);
        var state = job.GetState();
        Assert.AreEqual(IppJobState.Canceled, state.State);
        Assert.AreEqual("job-canceled-by-user", state.Reason);
        Assert.AreEqual("job-canceled-by-user", state.Message);

        job.Terminate(IppJobState.Aborted, "ignored");
        Assert.AreEqual(IppJobState.Canceled, job.GetState().State);
    }

    [TestMethod]
    public void IppJob_TerminateAfterAttach_IsIgnored()
    {
        var job = NewJob();
        job.Attach(NewTicket(), "application/pdf");

        job.Terminate(IppJobState.Canceled, "ignored");

        Assert.AreEqual(IppJobState.Pending, job.GetState().State);
    }

    // ---------------------------------------------------------------------
    // IppServerSettings / SharedPrinter / misc
    // ---------------------------------------------------------------------

    [TestMethod]
    public void IppServerSettings_DefaultsAndUpTime()
    {
        var settings = new IppServerSettings();

        Assert.AreEqual(5000, settings.WebPort);
        Assert.IsTrue(settings.WebUiEnabled);
        Assert.AreEqual(631, settings.IppPort);
        Assert.AreEqual(256L * 1024 * 1024, settings.MaxJobBytes);
        Assert.AreEqual(50, settings.MaxPendingJobs);
        Assert.IsNull(settings.MdnsHostName);
        Assert.IsTrue(settings.UpTimeSeconds >= 1);
        Assert.IsTrue(settings.ToUpTime(DateTime.UtcNow.AddSeconds(10)) >= 11);
        Assert.AreEqual(1, settings.ToUpTime(settings.StartedAt));
    }

    [TestMethod]
    public void SharedPrinter_ResourcePathAndTruncateUtf8()
    {
        var printer = new SharedPrinter
        {
            WindowsName = "HP LaserJet",
            Slug = "hp-laserjet",
            DisplayName = "Printman - HP LaserJet",
            Uuid = Guid.NewGuid()
        };

        Assert.AreEqual("ipp/print/hp-laserjet", printer.ResourcePath);
        Assert.AreEqual("abc", SharedPrinter.TruncateUtf8("abc", 10));
        Assert.AreEqual("ab", SharedPrinter.TruncateUtf8("ab cd", 3));
        Assert.AreEqual("caf", SharedPrinter.TruncateUtf8("caf\u00e9", 4));
    }

    [TestMethod]
    public void IppJobOptions_Defaults()
    {
        var options = new IppJobOptions();

        Assert.AreEqual(1, options.Copies);
        Assert.AreEqual(PrintDuplex.Default, options.Duplex);
        Assert.AreEqual(PrintColorMode.Default, options.ColorMode);
        Assert.AreEqual(PrintOrientation.Auto, options.Orientation);
        Assert.IsNull(options.PaperSizeName);
        Assert.IsTrue(options.PageRange.IsAllPages);
        Assert.IsTrue(options.FitToPage);
    }

    [TestMethod]
    public void IppResolution_DefaultUnitsAreDotsPerInch()
    {
        var resolution = new IppResolution(300, 300);

        Assert.AreEqual(3, resolution.Units);
    }

    [TestMethod]
    public void IppJobState_IsTerminal_MatchesCanceledAndBeyond()
    {
        Assert.IsFalse(IppJobState.IsTerminal(IppJobState.Pending));
        Assert.IsFalse(IppJobState.IsTerminal(IppJobState.PendingHeld));
        Assert.IsFalse(IppJobState.IsTerminal(IppJobState.Processing));
        Assert.IsFalse(IppJobState.IsTerminal(IppJobState.ProcessingStopped));
        Assert.IsTrue(IppJobState.IsTerminal(IppJobState.Canceled));
        Assert.IsTrue(IppJobState.IsTerminal(IppJobState.Aborted));
        Assert.IsTrue(IppJobState.IsTerminal(IppJobState.Completed));
    }

    [TestMethod]
    public void IppParseException_CarriesMessage()
    {
        var exception = new IppParseException("bad");

        Assert.AreEqual("bad", exception.Message);
    }

    private static IppJob NewJob() => new()
    {
        Id = 1,
        PrinterSlug = "printer",
        Name = "job",
        UserName = "alice"
    };

    private static PipelineTicket NewTicket() => new(new PipelineBatch());
}
