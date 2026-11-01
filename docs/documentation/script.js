document.addEventListener("DOMContentLoaded", () => {
    const rootElement = document.documentElement;
    const themeToggle = document.querySelector("#theme-toggle");
    const themeColorMeta = document.querySelector('meta[name="theme-color"]');
    const downloadLinks = [...document.querySelectorAll("[data-X-Notch-download]")];
    const searchInput = document.querySelector("#docs-search-input");
    const searchResults = document.querySelector("#docs-search-results");
    const searchRoot = document.querySelector("[data-search-root]");
    const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const themeColors = {
        light: "#f8f9fc",
        dark: "#0f1116"
    };

    document.querySelectorAll("pre > code[class*='language-']").forEach((codeBlock) => {
        const pre = codeBlock.parentElement;
        if (!pre) {
            return;
        }

        codeBlock.className.split(/\s+/).forEach((className) => {
            if (className.startsWith("language-")) {
                pre.classList.add(className);
            }
        });
    });

    if (window.Prism?.highlightAll) {
        window.Prism.highlightAll();
    }

    const applyThemeState = (isDark, persist = true) => {
        rootElement.classList.toggle("dark", isDark);

        if (persist) {
            localStorage.setItem("X-Notch-site-theme", isDark ? "dark" : "light");
        }

        if (themeToggle) {
            themeToggle.setAttribute("aria-pressed", isDark ? "true" : "false");
        }

        if (themeColorMeta) {
            themeColorMeta.setAttribute("content", isDark ? themeColors.dark : themeColors.light);
        }
    };

    const savedTheme = localStorage.getItem("X-Notch-site-theme");
    const prefersDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
    applyThemeState(savedTheme ? savedTheme === "dark" : prefersDark, false);

    const resolveLatestInstaller = async () => {
        if (!downloadLinks.length) {
            return;
        }

        try {
            const response = await fetch("https://api.github.com/repos/Gravit/X-Notch/releases/latest", {
                headers: {
                    Accept: "application/vnd.github+json"
                }
            });

            if (!response.ok) {
                throw new Error(`GitHub API returned ${response.status}`);
            }

            const release = await response.json();
            const assets = Array.isArray(release.assets) ? release.assets : [];
            const installerAsset = assets.find((asset) => /setup\.exe$/i.test(asset.name));

            if (!installerAsset?.browser_download_url) {
                throw new Error("No setup installer found in the latest release.");
            }

            downloadLinks.forEach((link) => {
                link.href = installerAsset.browser_download_url;
                link.removeAttribute("target");
                link.removeAttribute("rel");
                link.setAttribute("download", installerAsset.name);
                link.setAttribute("aria-label", `Download ${installerAsset.name}`);
                link.dataset.releaseVersion = release.tag_name || "";
            });
        } catch (error) {
            console.warn("X-Notch docs download link fallback in use.", error);
        }
    };

    void resolveLatestInstaller();

    const createThemeOverlay = (isDark, x, y) => {
        const overlay = document.createElement("div");
        overlay.className = "theme-transition-overlay";
        overlay.dataset.theme = isDark ? "dark" : "light";
        overlay.style.setProperty("--theme-x", `${x}px`);
        overlay.style.setProperty("--theme-y", `${y}px`);
        document.body.appendChild(overlay);
        return overlay;
    };

    let themeTransitionActive = false;

    const runThemeTransition = async (isDark, x, y) => {
        if (themeTransitionActive) {
            return;
        }

        themeTransitionActive = true;

        if (themeToggle) {
            themeToggle.disabled = true;
        }

        rootElement.classList.add("theme-transitioning");

        if (prefersReducedMotion) {
            applyThemeState(isDark);
            rootElement.classList.remove("theme-transitioning");

            if (themeToggle) {
                themeToggle.disabled = false;
            }

            themeTransitionActive = false;
            return;
        }

        const overlay = createThemeOverlay(isDark, x, y);
        const maxRadius = Math.hypot(
            Math.max(x, window.innerWidth - x),
            Math.max(y, window.innerHeight - y)
        );

        try {
            await overlay.animate([
                {
                    clipPath: `circle(0px at ${x}px ${y}px)`,
                    opacity: 1
                },
                {
                    clipPath: `circle(${maxRadius}px at ${x}px ${y}px)`,
                    opacity: 1
                }
            ], {
                duration: 360,
                easing: "cubic-bezier(0.2, 0.9, 0.18, 1)",
                fill: "forwards"
            }).finished;

            applyThemeState(isDark);

            await overlay.animate([
                { opacity: 1 },
                { opacity: 0 }
            ], {
                duration: 180,
                easing: "ease-out",
                fill: "forwards"
            }).finished;
        } finally {
            overlay.remove();
            rootElement.classList.remove("theme-transitioning");

            if (themeToggle) {
                themeToggle.disabled = false;
            }

            themeTransitionActive = false;
        }
    };

    if (themeToggle) {
        themeToggle.addEventListener("click", () => {
            const isDark = !rootElement.classList.contains("dark");
            const rect = themeToggle.getBoundingClientRect();
            const x = rect.left + rect.width / 2;
            const y = rect.top + rect.height / 2;
            void runThemeTransition(isDark, x, y);
        });
    }

    const sections = [...document.querySelectorAll("[data-doc-section]")];
    const navLinks = [...document.querySelectorAll('.docs-sidebar a[href^="#"], .docs-toc a[href^="#"]')];
    const linkGroups = new Map();

    navLinks.forEach((link) => {
        const target = link.getAttribute("href");
        if (!target) {
            return;
        }

        if (!linkGroups.has(target)) {
            linkGroups.set(target, []);
        }

        linkGroups.get(target).push(link);
    });

    const setActiveSection = (id) => {
        linkGroups.forEach((links, target) => {
            const isActive = target === `#${id}`;
            links.forEach((link) => {
                link.classList.toggle("is-active", isActive);
            });
        });
    };

    const sectionObserver = new IntersectionObserver((entries) => {
        const visible = entries
            .filter((entry) => entry.isIntersecting)
            .sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];

        if (visible?.target?.id) {
            setActiveSection(visible.target.id);
        }
    }, {
        rootMargin: "-20% 0px -60% 0px",
        threshold: [0.2, 0.4, 0.7]
    });

    sections.forEach((section) => {
        sectionObserver.observe(section);
    });

    if (sections[0]?.id) {
        setActiveSection(sections[0].id);
    }

    const searchIndex = navLinks
        .filter((link, index, links) => links.findIndex((candidate) => candidate.getAttribute("href") === link.getAttribute("href")) === index)
        .map((link) => {
            const href = link.getAttribute("href");
            const section = href ? document.querySelector(href) : null;
            const heading = section?.querySelector(".doc-heading");

            return {
                href,
                title: link.textContent.trim(),
                category: heading?.tagName === "H1" ? "Overview" : "Section"
            };
        })
        .filter((item) => item.href && item.title);

    let activeSearchIndex = -1;

    const hideSearchResults = () => {
        if (searchResults) {
            searchResults.hidden = true;
            searchResults.innerHTML = "";
        }

        activeSearchIndex = -1;
    };

    const navigateToSearchResult = (href) => {
        const target = document.querySelector(href);
        if (!target) {
            return;
        }

        hideSearchResults();
        target.scrollIntoView({ behavior: prefersReducedMotion ? "auto" : "smooth", block: "start" });
        history.replaceState(null, "", href);
    };

    const renderSearchResults = (matches) => {
        if (!searchResults) {
            return;
        }

        if (!matches.length) {
            searchResults.hidden = false;
            searchResults.innerHTML = '<div class="docs-search__result"><span>No matching sections</span><small>Try a broader term like plugin, install, or theme.</small></div>';
            activeSearchIndex = -1;
            return;
        }

        searchResults.hidden = false;
        searchResults.innerHTML = matches.map((match, index) => `
            <button type="button" class="docs-search__result${index === 0 ? " is-active" : ""}" data-search-href="${match.href}">
                <span>${match.title}</span>
                <small>${match.category}</small>
            </button>
        `).join("");
        activeSearchIndex = 0;
    };

    if (searchInput && searchResults) {
        searchInput.addEventListener("input", () => {
            const query = searchInput.value.trim().toLowerCase();

            if (!query) {
                hideSearchResults();
                return;
            }

            const matches = searchIndex
                .filter((entry) => entry.title.toLowerCase().includes(query))
                .slice(0, 8);

            renderSearchResults(matches);
        });

        searchInput.addEventListener("keydown", (event) => {
            const buttons = [...searchResults.querySelectorAll("[data-search-href]")];

            if (event.key === "Escape") {
                hideSearchResults();
                return;
            }

            if (!buttons.length) {
                return;
            }

            if (event.key === "ArrowDown") {
                event.preventDefault();
                activeSearchIndex = Math.min(activeSearchIndex + 1, buttons.length - 1);
            } else if (event.key === "ArrowUp") {
                event.preventDefault();
                activeSearchIndex = Math.max(activeSearchIndex - 1, 0);
            } else if (event.key === "Enter") {
                event.preventDefault();
                const activeButton = buttons[activeSearchIndex] || buttons[0];
                const href = activeButton.dataset.searchHref;
                if (href) {
                    navigateToSearchResult(href);
                }
                return;
            } else {
                return;
            }

            buttons.forEach((button, index) => {
                button.classList.toggle("is-active", index === activeSearchIndex);
            });
        });

        searchResults.addEventListener("click", (event) => {
            const button = event.target.closest("[data-search-href]");
            if (!button) {
                return;
            }

            const href = button.dataset.searchHref;
            if (href) {
                navigateToSearchResult(href);
            }
        });

        document.addEventListener("click", (event) => {
            if (!searchRoot?.contains(event.target)) {
                hideSearchResults();
            }
        });

        document.addEventListener("keydown", (event) => {
            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
                event.preventDefault();
                searchInput.focus();
                searchInput.select();
            }
        });
    }
});
