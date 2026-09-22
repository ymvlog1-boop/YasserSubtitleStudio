using Yasser.ResumeCore;

static void Help()
{
    Console.WriteLine("Yasser Subtitle Studio — checkpoint prototype (not the Subtitle Edit UI)");
    Console.WriteLine("init <project.yssproj> <existing.srt> <video.mp4>");
    Console.WriteLine("status <project.yssproj>");
    Console.WriteLine("position <project.yssproj> <milliseconds> <cue-guid-or-minus>");
    Console.WriteLine("edit <project.yssproj> <cue-guid> <translated text>");
    Console.WriteLine("queue <project.yssproj> <engine-identifier-and-settings>");
    Console.WriteLine("complete <project.yssproj> <work-guid> <translated text>");
    Console.WriteLine("export-partial <project.yssproj> <output.srt>");
    Console.WriteLine("transcribe-auto <project.yssproj> <video.mp4> <whisper-cli.exe> <multilingual-model.bin> [ffmpeg.exe] [ffprobe.exe]");
    Console.WriteLine("export-transcript <project.yssproj> <output.srt>");
}

try
{
    if (args.Length == 0) { Help(); return; }
    var store = new ProjectStore();
    var service = new ProjectService(store);
    var engine = new ResumeEngine(store);
    string command = args[0].ToLowerInvariant();
    if (command == "init" && args.Length == 4)
    {
        var project = service.CreateFromSrt(args[1], args[2], args[3]);
        Console.WriteLine($"Saved {project.Cues.Count} lines in {args[1]}");
    }
    else if (command == "status" && args.Length == 2)
    {
        var project = service.Open(args[1]);
        Console.WriteLine($"Project: {project.Name}; media: {project.VideoPath}; position: {project.VideoPositionMilliseconds}ms");
        foreach (var cue in project.Cues.OrderBy(c => c.StartMilliseconds))
            Console.WriteLine($"CUE {cue.Id} [{cue.StartMilliseconds}-{cue.EndMilliseconds}] edited={cue.ManuallyEdited} translated={cue.Translation}");
        foreach (var task in project.Work)
            Console.WriteLine($"TASK {task.Id} {task.Stage} {task.Status} [{task.StartMilliseconds}-{task.EndMilliseconds}] language={task.DetectedLanguageCode ?? "unknown"}");
    }
    else if (command == "position" && args.Length == 4)
    {
        var project = service.Open(args[1]);
        Guid? cue = args[3] == "-" ? null : Guid.Parse(args[3]);
        engine.SetPosition(args[1], project, long.Parse(args[2]), cue);
        Console.WriteLine("Playback position saved.");
    }
    else if (command == "edit" && args.Length == 4)
    {
        var project = service.Open(args[1]);
        engine.EditCue(args[1], project, Guid.Parse(args[2]), args[3]);
        Console.WriteLine("Human edit saved.");
    }
    else if (command == "queue" && args.Length == 3)
    {
        var project = service.Open(args[1]);
        var queued = engine.QueueTranslation(args[1], project, ResumeEngine.Fingerprint(args[2]));
        Console.WriteLine($"Translation tasks remaining: {queued.Count}");
        foreach (var task in queued) Console.WriteLine(task.Id);
    }
    else if (command == "complete" && args.Length == 4)
    {
        var project = service.Open(args[1]);
        engine.CompleteTranslation(args[1], project, Guid.Parse(args[2]), args[3]);
        Console.WriteLine("Translation checkpoint saved.");
    }
    else if (command == "export-partial" && args.Length == 3)
    {
        service.ExportPartial(args[1], args[2]);
        Console.WriteLine($"Partial SRT saved: {args[2]}");
    }
    else if (command == "transcribe-auto" && args.Length is >= 5 and <= 7)
    {
        var options = new AutoLanguageOptions(args.Length >= 6 ? args[5] : "ffmpeg",
            args.Length >= 7 ? args[6] : "ffprobe", args[3], args[4]);
        var transcriber = new AutoLanguageTranscriber(store);
        var project = await transcriber.RunAsync(args[1], args[2], options,
            (task, language) => Console.WriteLine($"Saved [{task.StartMilliseconds}-{task.EndMilliseconds}] detected={language ?? "unknown"}"));
        Console.WriteLine($"Transcribed {project.Cues.Count} cues. Run export-transcript for a partial/current SRT.");
    }
    else if (command == "export-transcript" && args.Length == 3)
    {
        service.ExportTranscriptPartial(args[1], args[2]);
        Console.WriteLine($"Transcript SRT saved: {args[2]}");
    }
    else
    {
        Help();
        Environment.ExitCode = 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    Environment.ExitCode = 1;
}
