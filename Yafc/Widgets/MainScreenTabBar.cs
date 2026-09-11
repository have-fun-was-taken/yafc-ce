using System;
using System.Numerics;
using SDL2;
using Yafc.Core;
using Yafc.I18n;
using Yafc.Model;
using Yafc.UI;

namespace Yafc;

public class MainScreenTabBar {
    /// <summary>The vertical distance between the tops of two adjacent rows of tabs. Tabs are slightly taller than this, and the excess is clipped.</summary>
    private const float rowHeight = 2.1f;
    /// <summary>The padding around the contents of one tab.</summary>
    private static readonly Padding tabPadding = new(0.5f, 0.2f, 0.2f, 0.5f);
    /// <summary>The spacing between the icon, the name, and the close button of one tab.</summary>
    private const float tabSpacing = 0.2f;
    private const float tabIconSize = 1.5f;
    private const float closeIconSize = 0.8f;
    /// <summary>The width of the close button: its icon plus the 0.3 padding <see cref="ImGuiUtils.BuildButton"/> adds on each side.</summary>
    private const float closeButtonWidth = closeIconSize + 0.3f + 0.3f;

    private readonly MainScreen screen;
    private readonly ImGui tabs;
    /// <summary>The number of rows the tabs were wrapped onto the last time they were built.</summary>
    private int rowCount = 1;
    /// <summary>The row limit the tabs were last built with, so a change to the preference can force them to be laid out again.</summary>
    private int lastMaxRows = Preferences.Instance.maxTabBarRows;
    public float maxScroll { get; private set; }

    public MainScreenTabBar(MainScreen screen) {
        this.screen = screen;
        tabs = new ImGui(BuildContents, default, RectAllocator.LeftAlign, true);
    }

    /// <summary>
    /// Predicts how wide <paramref name="page"/>'s tab will be, so the tab bar can tell whether it still fits on the current row.
    /// This must be kept in sync with the tab drawing code in <see cref="BuildContents"/>.
    /// </summary>
    private static float MeasureTabWidth(ImGui gui, ProjectPage page)
        => tabPadding.left + (page.icon == null ? 0f : tabIconSize) + tabSpacing + gui.GetTextDimensions(out _, page.name).X
            + tabSpacing + closeButtonWidth + tabPadding.right;

    private void BuildContents(ImGui gui) {
        gui.allocator = RectAllocator.LeftAlign;
        gui.spacing = 0f;
        int changePage = 0;
        ProjectPage? changePageTo = null;
        ProjectPage? prevPage = null;
        float right = 0f;
        var project = screen.project;
        // An infinite wrap width keeps all the tabs on one row, which is what the single-row (sideways-scrolling) tab bar wants.
        float wrapWidth = lastMaxRows > 1 ? MathF.Max(gui.width, 1f) : float.PositiveInfinity;
        float rowRight = 0f;
        rowCount = 1;
        var row = gui.EnterRow(0f, RectAllocator.LeftRow);

        try {
            for (int i = 0; i < project.displayPages.Count; i++) {
                var pageGuid = project.displayPages[i];
                var page = project.FindPage(pageGuid);
                if (page == null) {
                    continue;
                }

                if (changePage > 0 && changePageTo == null) {
                    changePageTo = page;
                }

                if (rowRight > 0f && rowRight + MeasureTabWidth(gui, page) > wrapWidth) {
                    row.Dispose();
                    // The row is a bit taller than rowHeight, so pull the next row up to keep the rows exactly rowHeight apart.
                    gui.AllocateSpacing(rowHeight - gui.lastRect.Height);
                    row = gui.EnterRow(0f, RectAllocator.LeftRow);
                    rowRight = 0f;
                    rowCount++;
                }

                bool isActive = screen.activePage == page;
                bool isSecondary = screen.secondaryPage == page;
                using (gui.EnterGroup(tabPadding)) {
                    gui.spacing = tabSpacing;
                    if (page.icon != null) {
                        gui.BuildIcon(page.icon.GetIcon(), tabIconSize);
                    }
                    else {
                        _ = gui.AllocateRect(0f, tabIconSize);
                    }

                    gui.BuildText(page.name);
                    if (gui.BuildButton(Icon.Close, size: closeIconSize)) {
                        if (isActive || isSecondary) {
                            changePageTo = prevPage;
                            changePage = isActive ? 1 : 2;
                        }

                        if (InputSystem.Instance.control && page.canDelete) {
                            project.RemovePage(page);
                        }
                        else {
                            screen.ClosePage(pageGuid);
                        }

                        i--;
                    }
                }

                rowRight = gui.lastRect.Right;
                right = MathF.Max(right, rowRight);

                if (gui.DoListReordering(gui.lastRect, gui.lastRect, i, out int from)) {
                    project.RecordUndo(true).displayPages.MoveListElementIndex(from, i);
                }

                if ((isActive || isSecondary) && gui.isBuilding) {
                    gui.DrawRectangle(new Rect(gui.lastRect.X, gui.lastRect.Bottom - 0.4f, gui.lastRect.Width, 0.4f), isActive ? SchemeColor.Primary : SchemeColor.Secondary);
                }

                var evt = gui.BuildButton(gui.lastRect, isActive ? SchemeColor.Background : SchemeColor.BackgroundAlt,
                    (isActive || isSecondary) ? SchemeColor.Background : SchemeColor.Grey, button: 0);

                if (evt == ButtonEvent.Click) {
                    if (gui.actionParameter == SDL.SDL_BUTTON_RIGHT) {
                        gui.window?.HideTooltip(); // otherwise it's displayed over the dropdown
                        gui.ShowDropDown(gui => PageRightClickDropdown(gui, page));
                    }
                    else {
                        changePage = InputSystem.Instance.control ? 2 : 1;
                        changePageTo = page;
                    }
                }
                else if (evt == ButtonEvent.MouseOver) {
                    screen.ShowTooltip(gui, page, false, gui.lastRect);
                }

                prevPage = page;
            }
        }
        finally {
            row.Dispose();
        }

        gui.SetMinWidth(right);

        if (changePage > 0) {
            if (changePage == 1) {
                if (changePageTo == screen.activePage) {
                    ProjectPageSettingsPanel.ShowEdit(changePageTo!);
                }
                else {
                    screen.SetActivePage(changePageTo);
                }
            }
            else {
                screen.SetSecondaryPage(changePageTo == screen.secondaryPage ? null : changePageTo);
            }
        }
    }

    private void PageRightClickDropdown(ImGui gui, ProjectPage page) {
        bool isSecondary = screen.secondaryPage == page;
        bool isActive = screen.activePage == page;
        if (gui.BuildContextMenuButton(LSs.EditPageProperties, icon: Icon.Edit)) {
            _ = gui.CloseDropdown();
            ProjectPageSettingsPanel.ShowEdit(page);
        }
        if (!isSecondary && !isActive) {
            if (gui.BuildContextMenuButton(LSs.OpenSecondaryPage, LSs.ShortcutCtrlClick, Icon.Secondary)) {
                _ = gui.CloseDropdown();
                screen.SetSecondaryPage(page);
            }
        }
        else if (isSecondary) {
            if (gui.BuildContextMenuButton(LSs.CloseSecondaryPage, LSs.ShortcutCtrlClick, Icon.Close)) {
                _ = gui.CloseDropdown();
                screen.SetSecondaryPage(null);
            }
        }
        if (gui.BuildContextMenuButton(LSs.DuplicatePage, icon: Icon.Copy)) {
            _ = gui.CloseDropdown();
            if (ProjectPageSettingsPanel.ClonePage(page) is { } copy) {
                screen.project.RecordUndo().pages.Add(copy);
                MainScreen.Instance.SetActivePage(copy);
            }
        }
        if (gui.BuildContextMenuButton(LSs.DeletePage, icon: Icon.Delete, disabled: !page.canDelete)) {
            _ = gui.CloseDropdown();
            if (page.canDelete) {
                screen.project.RemovePage(page);
            }
        }
    }

    public void Build(ImGui gui) {
        int maxRows = Preferences.Instance.maxTabBarRows;
        if (maxRows != lastMaxRows) {
            lastMaxRows = maxRows;
            tabs.Rebuild();
        }

        _ = gui.RemainingRow();
        var measuredSize = tabs.CalculateState(gui.width);
        var rect = gui.AllocateRect(0f, Math.Min(rowCount, maxRows) * rowHeight, RectAlignment.Full);
        // Wrapped tabs scroll up and down; a tab bar that is only one row tall scrolls sideways instead.
        bool scrollsVertically = rowCount > 1;

        switch (gui.action) {
            case ImGuiAction.Build:
                gui.DrawPanel(rect, tabs);
                maxScroll = scrollsVertically
                    ? MathF.Max(0f, (rowCount * rowHeight) - rect.Height)
                    : MathF.Max(0f, measuredSize.X - rect.Width);

                // Discard scrolling that is no longer reachable, e.g. after closing tabs, resizing the window, or switching scroll directions.
                Vector2 offset = scrollsVertically ? new(0f, MathF.Max(tabs.offset.Y, -maxScroll)) : new(MathF.Max(tabs.offset.X, -maxScroll), 0f);
                if (offset != tabs.offset) {
                    tabs.offset = offset;
                }
                break;
            case ImGuiAction.MouseScroll:
                if (gui.ConsumeEvent(rect)) {
                    if (scrollsVertically) {
                        float clampedY = MathUtils.Clamp(-tabs.offset.Y + (rowHeight * gui.actionParameter), 0, maxScroll);
                        tabs.offset = new Vector2(0f, -clampedY);
                    }
                    else {
                        float clampedX = MathUtils.Clamp(-tabs.offset.X + (6f * gui.actionParameter), 0, maxScroll);
                        tabs.offset = new Vector2(-clampedX, 0f);
                    }
                }
                break;
        }
    }
}
