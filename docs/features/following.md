# Following Teams and Athletes

The **Library > Follow** page monitors sports the way fans actually watch
them: by who is playing, not just which league it is. Follow a team or an
athlete and Sportarr discovers their competitions, adds the leagues you
pick, and monitors only the events that involve them.

## Follow a team

Available for Soccer, Basketball, Ice Hockey, Football, Baseball, Rugby,
Volleyball, Handball, Cricket, Australian Football, Netball, Field Hockey,
Lacrosse, and Gaelic.

1. Open **Library > Follow**, stay on the **Teams** tab
2. Search or filter for your team and hit **Follow**
3. Expand the team to discover every league it plays in
4. Pick the leagues you care about and add them

Sportarr adds each league with team scoping, so only your team's events are
monitored, including cups and international competitions the team appears
in.

## Follow an athlete

Switch to the **Athletes** tab and search by name. Two kinds of athletes
work differently under the hood:

- **Fighters** (UFC and other combat sports) are tracked per event. Adding
  their promotion monitors exactly the cards they appear on, nothing else,
  and new bookings are picked up automatically on the next league refresh.
- **Team-sport athletes** follow through their current team. Sportarr
  resolves the team from roster data when you expand the athlete, so if
  they get traded, the next discovery follows them to the new team.

!!! tip
    Following an athlete's promotion does not blanket-monitor it. A UFC
    added through a followed fighter monitors only that fighter's cards.
    If you later want the whole promotion, open the league and change its
    Monitor setting.

## Games your teams are not in

A league you follow by team normally stores only your teams' games. **Keep
every game in the library**, in the league's edit dialog, stores the rest as
well, unmonitored, so you can find a one-off game and monitor it yourself.

Turning the setting off stops new games arriving, but the games it already
stored stay where they are. When that leaves games behind, a line appears
under the setting saying how many, with a link to remove them. It only
appears when there is something to remove.

Removing takes the games the league would not store today. These always
stay:

- games you monitored yourself, including ones a followed athlete plays in
- games holding a file
- games with a download in flight, a recording scheduled, or a release
  waiting out a delay
- games you added by hand, which no sync can fetch back

The confirmation says how many stay and why. Everything else comes back if
you turn the setting on again and run a Deep Sync, because those games come
from sportarr.net rather than from your install.

!!! tip
    A game you monitor yourself keeps its place even when it is nobody you
    follow. Switching the league off and on again, or changing its settings,
    leaves it alone. Unmonitoring it yourself gives it up again.

## How it stays current

Every league refresh re-checks followed athletes and monitors any newly
announced events they appear on. Unfollowing a team or athlete stops future
monitoring but leaves already-monitored events alone, so nothing you
collected disappears.
