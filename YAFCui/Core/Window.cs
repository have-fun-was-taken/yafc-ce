﻿using System;
using System.Numerics;
using SDL2;

namespace YAFC.UI {
    public abstract class Window : IDisposable {
        public readonly ImGui rootGui;
        internal IntPtr window;
        internal Vector2 contentSize;
        internal uint id;
        internal bool repaintRequired = true;
        internal bool visible;
        internal bool closed;
        internal long nextRepaintTime = long.MaxValue;
        internal float pixelsPerUnit;
        public virtual SchemeColor backgroundColor => SchemeColor.Background;

        private Tooltip tooltip;
        private SimpleTooltip simpleTooltip;
        protected DropDownPanel dropDown;
        private SimpleDropDown simpleDropDown;
        private ImGui.DragOverlay draggingOverlay;
        public DrawingSurface surface { get; protected set; }

        public int displayIndex => SDL.SDL_GetWindowDisplayIndex(window);
        public int repaintCount { get; private set; }

        public Vector2 size => contentSize;

        public virtual bool preventQuit => false;
        internal Window(Padding padding) {
            rootGui = new ImGui(Build, padding);
        }

        internal void Create() {
            _ = SDL.SDL_SetRenderDrawBlendMode(surface.renderer, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND);
            id = SDL.SDL_GetWindowID(window);
            Ui.CloseWidowOfType(GetType());
            Ui.RegisterWindow(id, this);
            visible = true;
        }

        internal int CalculateUnitsToPixels(int display) {
            _ = SDL.SDL_GetDisplayDPI(display, out float dpi, out _, out _);
            _ = SDL.SDL_GetDisplayBounds(display, out SDL.SDL_Rect rect);
            // 82x60 is the minimum screen size in units, plus some for borders
            int desiredUnitsToPixels = dpi == 0 ? 13 : MathUtils.Round(dpi / 6.8f);
            if (desiredUnitsToPixels * 82f >= rect.w) {
                desiredUnitsToPixels = MathUtils.Floor(rect.w / 82f);
            }

            if (desiredUnitsToPixels * 65f >= rect.h) {
                desiredUnitsToPixels = MathUtils.Floor(rect.h / 65f);
            }

            return desiredUnitsToPixels;
        }

        internal virtual void WindowResize() {
            rootGui.Rebuild();
        }

        internal void WindowMoved() {
            int index = SDL.SDL_GetWindowDisplayIndex(window);
            int u2p = CalculateUnitsToPixels(index);
            if (u2p != pixelsPerUnit) {
                pixelsPerUnit = u2p;
                surface.pixelsPerUnit = pixelsPerUnit;
                repaintRequired = true;
                rootGui.MarkEverythingForRebuild();
                WindowResize();
            }
        }

        protected virtual void OnRepaint() { }

        internal void Render() {
            if (!repaintRequired && nextRepaintTime > Ui.time) {
                return;
            }

            if (nextRepaintTime <= Ui.time) {
                nextRepaintTime = long.MaxValue;
            }

            OnRepaint();
            repaintRequired = false;
            if (rootGui.IsRebuildRequired()) {
                _ = rootGui.CalculateState(size.X, pixelsPerUnit);
            }

            MainRender();
            surface.Present();
        }

        protected virtual void MainRender() {
            SDL.SDL_Color bgColor = backgroundColor.ToSdlColor();
            _ = SDL.SDL_SetRenderDrawColor(surface.renderer, bgColor.r, bgColor.g, bgColor.b, bgColor.a);
            Rect fullRect = new Rect(default, contentSize);
            repaintCount++;
            surface.Clear(rootGui.ToSdlRect(fullRect));
            rootGui.InternalPresent(surface, fullRect, fullRect);
        }

        public IPanel HitTest(Vector2 position) {
            return rootGui.HitTest(position);
        }

        public void Rebuild() {
            rootGui.Rebuild();
        }

        public void Repaint() {
            if (closed) {
                return;
            }

            if (!Ui.IsMainThread()) {
                throw new NotSupportedException("This should be called from the main thread");
            }

            repaintRequired = true;
        }

        protected internal virtual void Close() {
            visible = false;
            closed = true;
            surface.Dispose();
            SDL.SDL_DestroyWindow(window);
            Dispose();
            window = IntPtr.Zero;
            Ui.UnregisterWindow(this);
        }

        private void Focus() {
            if (window != IntPtr.Zero) {
                SDL.SDL_RaiseWindow(window);
                SDL.SDL_RestoreWindow(window);
                _ = SDL.SDL_SetWindowInputFocus(window);
            }
        }


        public virtual void FocusLost() { }
        public virtual void Minimized() { }

        public void SetNextRepaint(long nextRepaintTime) {
            if (this.nextRepaintTime > nextRepaintTime) {
                this.nextRepaintTime = nextRepaintTime;
            }
        }

        public void ShowTooltip(Tooltip tooltip) {
            this.tooltip = tooltip;
            Rebuild();
        }

        public void ShowTooltip(ImGui targetGui, Rect target, GuiBuilder builder, float width = 20f) {
            simpleTooltip ??= new SimpleTooltip();
            simpleTooltip.Show(builder, targetGui, target, width);
            ShowTooltip(simpleTooltip);

        }

        public void ShowDropDown(DropDownPanel dropDown) {
            this.dropDown = dropDown;
            Rebuild();
        }

        public void ShowDropDown(ImGui targetGui, Rect target, GuiBuilder builder, Padding padding, float width = 20f) {
            simpleDropDown ??= new SimpleDropDown();
            simpleDropDown.SetPadding(padding);
            simpleDropDown.SetFocus(targetGui, target, builder, width);
            ShowDropDown(simpleDropDown);
        }

        private void Build(ImGui gui) {
            if (closed) {
                return;
            }

            BuildContents(gui);
            if (dropDown != null) {
                dropDown.Build(gui);
                if (!dropDown.active) {
                    dropDown = null;
                }
            }
            draggingOverlay?.Build(gui);
            if (tooltip != null) {
                tooltip.Build(gui);
                if (!tooltip.active) {
                    tooltip = null;
                }
            }
        }

        protected abstract void BuildContents(ImGui gui);
        public virtual void Dispose() {
            rootGui.Dispose();
        }

        internal ImGui.DragOverlay GetDragOverlay() {
            return draggingOverlay ??= new ImGui.DragOverlay();
        }
    }
}
