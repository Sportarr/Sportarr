/*
 * Two things the theme cannot do on its own.
 *
 * The header should say where you are as soon as a page opens. Material
 * fills its second header topic from the page title, swaps it in only once
 * you scroll past the H1, and never fills it at all on a page without one,
 * so the header can sit on the site name for a whole page. The page name
 * goes into the first topic instead, on every navigation.
 *
 * The logo should introduce itself once a visit. Material rebuilds the
 * header on every navigation, even the instant ones, and a fresh element
 * restarts the intro inside the animated file. So the still artwork takes
 * over from the second page on. The rebuild does not always land before
 * this runs, hence the observer as well as the direct swap.
 */
var siteName = null
var visits = 0
var watching = false

function useStillLogo() {
  var logo = document.querySelector('.md-logo img')
  if (!logo) return
  var src = logo.getAttribute('src') || ''
  if (src.indexOf('logo-lockup.svg') !== -1) {
    logo.setAttribute('src', src.replace('logo-lockup.svg', 'logo-lockup-still.svg'))
  }
}

document$.subscribe(function () {
  visits += 1

  if (visits > 1) {
    useStillLogo()
    if (!watching) {
      watching = true
      new MutationObserver(useStillLogo).observe(document.body, {
        childList: true,
        subtree: true,
      })
    }
  }

  var topic = document.querySelector('.md-header__topic .md-ellipsis')
  if (!topic) return

  // Captured before the first overwrite, since the header survives an
  // instant navigation and would otherwise report our own last answer.
  if (siteName === null) siteName = topic.textContent.trim()

  var heading = document.querySelector('.md-content h1')
  var name = ''
  if (heading) {
    var text = heading.cloneNode(true)
    text.querySelectorAll('.headerlink').forEach(function (link) {
      link.remove()
    })
    name = text.textContent.trim()
  }

  // The landing page repeats the site name, which the logo already carries.
  topic.textContent = !name || name === siteName ? 'Wiki' : name
})
