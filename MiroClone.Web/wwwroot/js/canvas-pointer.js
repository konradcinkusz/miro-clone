// Pointer capture, coordinates and wheel for the board surface (ADR 0026). The only JavaScript in the app. It forwards
// events to the circuit and keeps no state: every rule about the canvas lives in C#.

export function attach(surface, target) {
  const local = (event) => {
    const box = surface.getBoundingClientRect();
    return [event.clientX - box.left, event.clientY - box.top];
  };
  // A closed circuit rejects the call. The page is gone by then, so there is nothing to report to.
  const send = (method, ...args) => target.invokeMethodAsync(method, ...args).catch(() => {});
  const pointer = (phase) => (event) => {
    if (phase === "down") {
      surface.setPointerCapture(event.pointerId);
    }
    const [x, y] = local(event);
    send("OnPointer", phase, x, y, event.button, event.shiftKey);
  };
  const wheel = (event) => {
    event.preventDefault();
    const [x, y] = local(event);
    send("OnWheel", x, y, event.deltaX, event.deltaY, event.ctrlKey || event.metaKey);
  };
  const listeners = {
    pointerdown: pointer("down"),
    pointermove: pointer("move"),
    pointerup: pointer("up"),
    pointercancel: pointer("cancel"),
    wheel,
  };
  for (const [type, listener] of Object.entries(listeners)) {
    surface.addEventListener(type, listener, { passive: false });
  }
  return {
    dispose() {
      for (const [type, listener] of Object.entries(listeners)) {
        surface.removeEventListener(type, listener);
      }
    },
  };
}
