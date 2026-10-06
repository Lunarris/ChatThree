namespace ChatThree.Ui.SettingsTabs;

public interface ISettingsTab
{
    string Name { get; }
    void Draw(bool changed);
}
