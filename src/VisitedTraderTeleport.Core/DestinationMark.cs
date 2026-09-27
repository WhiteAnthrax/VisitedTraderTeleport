namespace VisitedTraderTeleport;

// A player's private mark on a destination, so several traders with the same name can be
// told apart in the list. Marks never leave the client: they need no net package, and a
// packet is the one cost this mod cannot spend cheaply - adding a package type locks out
// every client that has not updated (see docs/ProtocolVersioning.md).
//
// The names are colours rather than meanings ("base", "shopping") because the player
// decides what each one means and no translation has to guess for them.
internal enum DestinationMark
{
    None = 0,
    Red = 1,
    Blue = 2,
    Green = 3,
    Yellow = 4,
}
