namespace ProcessAst.Migration;

public sealed class BusinessExecutionIntentFactory
{
    public BusinessExecutionIntent Create(BusinessMessageConfiguration config)
    {
        if (config is null)
            throw new ArgumentNullException(nameof(config));

        var intent = new BusinessExecutionIntent
        {
            MessageId = config.MessageId,
            ProcessReference = config.InternalServiceName,
            ExtendedRequestFormat = config.ExtendedRequestFormat,
            ResponseExpected = config.ReturnResponse,
            InvocationStyle = InferInvocationStyle(config)
        };

        foreach (var sheet in config.BackendSheets
                     .OrderBy(x => x.Level)
                     .ThenBy(x => x.PriorityInLevel))
        {
            var req = sheet.BackendRequest;

            intent.BackendPlans.Add(new BackendInvocationPlan
            {
                Level = sheet.Level,
                PriorityInLevel = sheet.PriorityInLevel,
                SheetId = sheet.SheetId,
                IsMandatory = sheet.IsMandatory,
                BackendName = req?.BackendName ?? "",
                RequestId = req?.RequestId ?? "",
                WaitForResponse = req?.WaitForResponse ?? false,
                IgnoreResponse = req?.IgnoreResponse ?? false,
                ConvertRequest = req?.ConvertBackendRequest ?? false,
                ConvertResponse = req?.ConvertBackendResponse ?? false
            });
        }

        return intent;
    }

    private static InvocationStyle InferInvocationStyle(BusinessMessageConfiguration config)
    {
        if (config.ReturnResponse &&
            config.BackendSheets.Any(x => x.BackendRequest?.WaitForResponse == true &&
                                          x.BackendRequest?.IgnoreResponse == false))
        {
            return InvocationStyle.SynchronousRequestResponse;
        }

        if (!config.ReturnResponse &&
            config.BackendSheets.Any(x => x.BackendRequest?.WaitForResponse == false))
        {
            return InvocationStyle.AsynchronousCommand;
        }

        if (!config.ReturnResponse && config.BackendSheets.Count == 0)
        {
            return InvocationStyle.EventDriven;
        }

        return InvocationStyle.Unknown;
    }
}
