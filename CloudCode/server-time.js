// Cloud Code script "server-time" — returns the server's current Unix time in milliseconds.
// The game uses it as the trusted clock for construction, research, training and healing timers,
// so changing the phone's clock can't finish them early.
//
// Dashboard: Cloud Code ▸ Scripts ▸ Create script ▸ name "server-time", type API, no parameters,
// paste this file, Save, then Publish.
module.exports = async () => {
  return Date.now();
};
