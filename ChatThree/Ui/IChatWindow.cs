using System.Numerics;
using ChatThree.GameFunctions.Types;

namespace ChatThree.Ui;

public interface IChatWindow
{
    Vector2 LastWindowPos { get; set; }
    Vector2 LastWindowSize { get; set; }
    HideState CurrentHideState { get; set; }
}