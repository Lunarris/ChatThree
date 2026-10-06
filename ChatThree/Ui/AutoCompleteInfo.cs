namespace ChatThree.Ui;

public class AutoCompleteInfo
{
    public string ToComplete;
    public int StartPos { get; }
    public int EndPos { get; }

    public AutoCompleteInfo(string toComplete, int startPos, int endPos)
    {
        ToComplete = toComplete;
        StartPos = startPos;
        EndPos = endPos;
    }
}
