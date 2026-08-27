/*
 * The header should say where you are as soon as a page opens.
 *
 * Material fills its second header topic from the page title, swaps it in
 * only once you scroll past the H1, and never fills it at all on a page
 * without one, so the header can sit on the site name for a whole page.
 * The page name goes into the first topic instead, on every navigation,
 * including the instant ones that never reload the document.
 */
var siteName = null

document$.subscribe(function () {
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
