const clamp = (value, min = 0, max = 1) => Math.min(max, Math.max(min, value));
const lerp = (start, end, amount) => start + (end - start) * amount;
const easeOutCubic = (value) => 1 - Math.pow(1 - value, 3);
const easeInOutCubic = (value) => (
    value < 0.5
        ? 4 * value * value * value
        : 1 - Math.pow(-2 * value + 2, 3) / 2
);

document.addEventListener("DOMContentLoaded", () => {
    const rootElement = document.documentElement;
    const themeToggle = document.querySelector("#theme-toggle");
    const themeColorMeta = document.querySelector('meta[name="theme-color"]');
    const header = document.querySelector(".site-header");
    const downloadLinks = [...document.querySelectorAll("[data-X-Notch-download]")];
    const pluginLibraryTriggers = [...document.querySelectorAll("[data-open-plugin-library]")];
    const pluginLibraryModal = document.querySelector("[data-plugin-library-modal]");
    const pluginLibraryGrid = pluginLibraryModal?.querySelector("[data-plugin-library-grid]");
    const pluginLibraryLoading = pluginLibraryModal?.querySelector("[data-plugin-library-loading]");
    const pluginLibraryError = pluginLibraryModal?.querySelector("[data-plugin-library-error]");
    const pluginLibrarySubtitle = pluginLibraryModal?.querySelector("[data-plugin-library-subtitle]");
    const pluginLibraryClosers = [...document.querySelectorAll("[data-close-plugin-library]")];
    const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    let themeTransitionActive = false;
    let pluginLibraryCache = null;
    let pluginLibraryFetchPromise = null;
    let lastPluginLibraryTrigger = null;
    let pluginLibraryOpenSequence = 0;
    const themeColors = {
        light: "#f6f7fb",
        dark: "#090b10"
    };

    const applyThemeState = (isDark, persist = true) => {
        rootElement.classList.toggle("dark", isDark);

        if (persist) {
            localStorage.setItem("X-Notch-site-theme", isDark ? "dark" : "light");
        }

        if (themeToggle) {
            themeToggle.checked = isDark;
        }

        if (themeColorMeta) {
            themeColorMeta.setAttribute("content", isDark ? themeColors.dark : themeColors.light);
        }
    };

    const savedTheme = localStorage.getItem("X-Notch-site-theme");
    const prefersDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
    const initialDark = savedTheme ? savedTheme === "dark" : prefersDark;
    applyThemeState(initialDark, false);

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
            console.warn("X-Notch download link fallback in use.", error);
        }
    };

    void resolveLatestInstaller();

    const escapeHtml = (value) => String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");

    const formatPluginDate = (value) => {
        if (!value) {
            return "";
        }

        const parsed = new Date(value);
        if (Number.isNaN(parsed.getTime())) {
            return "";
        }

        return new Intl.DateTimeFormat("en", {
            month: "short",
            day: "numeric",
            year: "numeric"
        }).format(parsed);
    };

    const wait = (ms) => new Promise((resolve) => {
        window.setTimeout(resolve, ms);
    });

    const getPluginLibraryFakeDelayMs = () => 2000 + Math.floor(Math.random() * 3001);

    const renderPluginLibraryCards = (plugins) => {
        if (!pluginLibraryGrid) {
            return;
        }

        pluginLibraryGrid.innerHTML = plugins.map((plugin) => {
            const permissions = Array.isArray(plugin.permissions) ? plugin.permissions.filter(Boolean) : [];
            const released = formatPluginDate(plugin.releaseDate);
            const metaBadges = [
                `Min X-Notch ${escapeHtml(plugin.minimumX-NotchVersion || "n/a")}`,
                escapeHtml(plugin.author || "Unknown author"),
                ...(permissions.length ? [`Permissions: ${escapeHtml(permissions.join(", "))}`] : []),
                ...(released ? [`Released ${escapeHtml(released)}`] : [])
            ];

            return `
                <article class="plugin-library-card">
                    <div class="plugin-library-card__top">
                        <div class="plugin-library-card__eyebrow">
                            <p>${escapeHtml(plugin.category || "Other")}</p>
                            ${plugin.isVerified ? '<span class="plugin-library-card__verified">Verified</span>' : ""}
                        </div>

                        <div class="plugin-library-card__title">
                            <h3>${escapeHtml(plugin.name || "Unnamed plugin")}</h3>
                            <span class="plugin-library-card__version">v${escapeHtml(plugin.version || "0.0.0")}</span>
                        </div>

                        <p class="plugin-library-card__description">${escapeHtml(plugin.description || "No description provided.")}</p>
                    </div>

                    <div class="plugin-library-card__meta">
                        ${metaBadges.map((badge) => `<span>${badge}</span>`).join("")}
                    </div>

                    <div class="plugin-library-card__actions">
                        <a class="plugin-library-card__action" href="${escapeHtml(plugin.downloadUrl || "#")}" target="_blank" rel="noreferrer">Download DLL</a>
                        <a class="plugin-library-card__action plugin-library-card__action--ghost" href="${escapeHtml(plugin.homepage || "https://github.com/Gravit/X-Notch-Plugins")}" target="_blank" rel="noreferrer">Open page</a>
                    </div>
                </article>
            `;
        }).join("");
    };

    const setPluginLibraryView = (state, plugins = []) => {
        if (!pluginLibraryModal || !pluginLibraryLoading || !pluginLibraryError || !pluginLibraryGrid || !pluginLibrarySubtitle) {
            return;
        }

        pluginLibraryModal.dataset.libraryState = state;
        pluginLibraryLoading.hidden = state !== "loading";
        pluginLibraryError.hidden = state !== "error";
        pluginLibraryGrid.hidden = state !== "ready";
        pluginLibraryLoading.style.display = state === "loading" ? "grid" : "none";
        pluginLibraryError.style.display = state === "error" ? "grid" : "none";
        pluginLibraryGrid.style.display = state === "ready" ? "grid" : "none";

        if (state === "ready") {
            renderPluginLibraryCards(plugins);
            pluginLibrarySubtitle.textContent = `${plugins.length} published plugins currently available in the official X-Notch browser library.`;
        } else if (state === "error") {
            pluginLibrarySubtitle.textContent = "The live library could not be loaded right now. You can still open the repository directly.";
        } else {
            pluginLibrarySubtitle.textContent = "Loading the latest published plugins from the official X-Notch library.";
        }
    };

    const loadPluginLibrary = async () => {
        if (pluginLibraryCache) {
            return pluginLibraryCache;
        }

        if (pluginLibraryFetchPromise) {
            return pluginLibraryFetchPromise;
        }

        pluginLibraryFetchPromise = fetch(`https://raw.githubusercontent.com/Gravit/X-Notch-Plugins/main/library.json?v=${Date.now()}`)
            .then((response) => {
                if (!response.ok) {
                    throw new Error(`Plugin library request failed with ${response.status}`);
                }

                return response.json();
            })
            .then((library) => {
                const plugins = Array.isArray(library.plugins) ? library.plugins : [];
                pluginLibraryCache = plugins;
                return plugins;
            })
            .finally(() => {
                pluginLibraryFetchPromise = null;
            });

        return pluginLibraryFetchPromise;
    };

    const openPluginLibrary = async (trigger) => {
        if (!pluginLibraryModal) {
            return;
        }

        const openSequence = ++pluginLibraryOpenSequence;
        lastPluginLibraryTrigger = trigger ?? document.activeElement;
        pluginLibraryModal.hidden = false;
        document.body.classList.add("plugin-library-open");
        setPluginLibraryView("loading");

        const delayPromise = wait(getPluginLibraryFakeDelayMs());

        try {
            const plugins = await loadPluginLibrary();

            await delayPromise;

            if (openSequence !== pluginLibraryOpenSequence || pluginLibraryModal.hidden) {
                return;
            }

            setPluginLibraryView("ready", plugins);
        } catch (error) {
            await delayPromise;

            if (openSequence !== pluginLibraryOpenSequence || pluginLibraryModal.hidden) {
                return;
            }

            console.warn("X-Notch plugin library modal fallback in use.", error);
            setPluginLibraryView("error");
        }
    };

    const closePluginLibrary = () => {
        if (!pluginLibraryModal || pluginLibraryModal.hidden) {
            return;
        }

        pluginLibraryModal.hidden = true;
        delete pluginLibraryModal.dataset.libraryState;
        document.body.classList.remove("plugin-library-open");

        if (lastPluginLibraryTrigger instanceof HTMLElement) {
            lastPluginLibraryTrigger.focus({ preventScroll: true });
        }
    };

    pluginLibraryTriggers.forEach((trigger) => {
        trigger.addEventListener("click", () => {
            void openPluginLibrary(trigger);
        });
    });

    pluginLibraryClosers.forEach((closer) => {
        closer.addEventListener("click", closePluginLibrary);
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            closePluginLibrary();
        }
    });

    const pauseThemeVideos = () => {
        if (prefersReducedMotion) {
            return [];
        }

        const activeVideos = [];
        document.querySelectorAll(".demo-video").forEach((video) => {
            if (!video.paused) {
                video.pause();
                activeVideos.push(video);
            }
        });

        return activeVideos;
    };

    const resumeThemeVideos = (videos) => {
        if (prefersReducedMotion) {
            return;
        }

        videos.forEach((video) => {
            if (video.dataset.ready === "true") {
                video.play().catch(() => {});
            }
        });
    };

    const runThemeTransition = async (isDark, x, y) => {
        if (themeTransitionActive) {
            return;
        }

        themeTransitionActive = true;

        if (themeToggle) {
            themeToggle.disabled = true;
        }

        const activeVideos = pauseThemeVideos();
        rootElement.classList.add("theme-transitioning");
        rootElement.style.setProperty("--x", `${x}px`);
        rootElement.style.setProperty("--y", `${y}px`);

        if (prefersReducedMotion || !document.startViewTransition) {
            applyThemeState(isDark);
            measureStoryScenes();
            rootElement.classList.remove("theme-transitioning");
            resumeThemeVideos(activeVideos);

            if (themeToggle) {
                themeToggle.disabled = false;
            }

            themeTransitionActive = false;
            return;
        }

        try {
            const transition = document.startViewTransition(() => {
                applyThemeState(isDark);
                measureStoryScenes();
            });

            await transition.ready;
            rootElement.style.setProperty("--x", `${x}px`);
            rootElement.style.setProperty("--y", `${y}px`);
            await transition.finished;
        } finally {
            rootElement.classList.remove("theme-transitioning");
            resumeThemeVideos(activeVideos);

            if (themeToggle) {
                themeToggle.disabled = false;
            }

            themeTransitionActive = false;
        }
    };

    if (themeToggle) {
        themeToggle.addEventListener("input", () => {
            const isDark = themeToggle.checked;
            const toggleRoot = themeToggle.closest(".theme-toggle");
            let x = window.innerWidth / 2;
            let y = window.innerHeight / 2;

            if (toggleRoot) {
                const rect = toggleRoot.getBoundingClientRect();
                x = rect.left + rect.width / 2;
                y = rect.top + rect.height / 2;
            }

            rootElement.style.setProperty("--x", `${x}px`);
            rootElement.style.setProperty("--y", `${y}px`);
            void runThemeTransition(isDark, x, y);
        });
    }

    const revealObserver = new IntersectionObserver((entries) => {
        for (const entry of entries) {
            if (entry.isIntersecting) {
                entry.target.classList.add("is-visible");
            }
        }
    }, {
        threshold: 0.14,
        rootMargin: "0px 0px -10% 0px"
    });

    document.querySelectorAll(".reveal").forEach((element) => {
        revealObserver.observe(element);
    });

    const videoObserver = new IntersectionObserver((entries) => {
        for (const entry of entries) {
            const video = entry.target;
            const start = Number(video.dataset.start || "0");

            if (entry.isIntersecting) {
                if (video.dataset.ready === "true" && video.currentTime < start) {
                    video.currentTime = start;
                }

                if (!prefersReducedMotion) {
                    video.play().catch(() => {});
                }
            } else {
                video.pause();
            }
        }
    }, {
        threshold: 0.42
    });

    document.querySelectorAll(".demo-video").forEach((video) => {
        const start = Number(video.dataset.start || "0");
        const shell = video.closest(".video-shell");

        if (video.dataset.position) {
            video.style.setProperty("--video-position", video.dataset.position);
        }

        const seekToStart = () => {
            try {
                if (shell && video.videoWidth && video.videoHeight) {
                    if (video.dataset.shell === "hud") {
                        shell.classList.add("video-shell--hud");
                    } else {
                        shell.style.setProperty("--shell-ratio", `${video.videoWidth} / ${video.videoHeight}`);
                    }
                }

                if (video.duration && start < video.duration) {
                    video.currentTime = start;
                }
                video.dataset.ready = "true";

                if (!prefersReducedMotion) {
                    video.play().catch(() => {});
                }
            } catch {
                video.dataset.ready = "true";
            }
        };

        video.addEventListener("loadedmetadata", seekToStart, { once: true });
        video.addEventListener("ended", () => {
            video.currentTime = start;
            if (!prefersReducedMotion) {
                video.play().catch(() => {});
            }
        });

        videoObserver.observe(video);
    });

    const storyScenes = [...document.querySelectorAll("[data-story-scene]")].map((section) => ({
        section,
        media: section.querySelector(".story-media"),
        copy: section.querySelector(".story-copy"),
        copyShiftStart: section.classList.contains("story-scene--reverse") ? 78 : -78
    }));

    let sceneMeasurements = [];
    let storyTicking = false;

    const canAnimateScenes = () => !prefersReducedMotion && window.innerWidth > 1080;

    const resetStoryScene = (scene) => {
        if (!scene.media || !scene.copy) {
            return;
        }

        scene.media.style.transform = "none";
        scene.media.style.borderRadius = "";
        scene.media.style.boxShadow = "";
        scene.copy.style.setProperty("--story-copy-opacity", "1");
        scene.copy.style.setProperty("--story-copy-shift-x", "0px");
        scene.copy.style.setProperty("--story-copy-shift-y", "0px");
    };

    const updateStoryScenes = () => {
        if (!canAnimateScenes()) {
            storyScenes.forEach(resetStoryScene);
            return;
        }

        for (const scene of sceneMeasurements) {
            const rect = scene.section.getBoundingClientRect();
            const rawProgress = clamp(-rect.top / Math.max(1, rect.height - window.innerHeight), 0, 1);
            const mediaProgress = easeInOutCubic(clamp((rawProgress - 0.03) / 0.68, 0, 1));
            const copyProgress = easeOutCubic(clamp((rawProgress - 0.2) / 0.34, 0, 1));
            const translateX = lerp(scene.startTranslateX, 0, mediaProgress);
            const translateY = lerp(scene.startTranslateY, 0, mediaProgress);
            const scale = lerp(scene.startScale, 1, mediaProgress);
            const copyShiftX = lerp(scene.copyShiftStart, 0, copyProgress);
            const copyShiftY = lerp(32, 0, copyProgress);
            const shadowY = lerp(60, 26, mediaProgress);
            const shadowBlur = lerp(140, 80, mediaProgress);
            const shadowAlpha = rootElement.classList.contains("dark")
                ? lerp(0.44, 0.28, mediaProgress)
                : lerp(0.11, 0.06, mediaProgress);

            scene.media.style.transform = `translate3d(${translateX.toFixed(2)}px, ${translateY.toFixed(2)}px, 0) scale(${scale.toFixed(4)})`;
            scene.media.style.borderRadius = `${lerp(18, 0, mediaProgress).toFixed(2)}px`;
            scene.media.style.boxShadow = `0 ${shadowY.toFixed(2)}px ${shadowBlur.toFixed(2)}px rgba(15, 18, 29, ${shadowAlpha.toFixed(3)})`;
            scene.copy.style.setProperty("--story-copy-opacity", copyProgress.toFixed(3));
            scene.copy.style.setProperty("--story-copy-shift-x", `${copyShiftX.toFixed(2)}px`);
            scene.copy.style.setProperty("--story-copy-shift-y", `${copyShiftY.toFixed(2)}px`);
        }
    };

    const measureStoryScenes = () => {
        sceneMeasurements = [];

        if (!canAnimateScenes()) {
            storyScenes.forEach(resetStoryScene);
            return;
        }

        for (const scene of storyScenes) {
            if (!scene.media || !scene.copy) {
                continue;
            }

            resetStoryScene(scene);

            const finalRect = scene.media.getBoundingClientRect();
            if (!finalRect.width || !finalRect.height) {
                continue;
            }

            const edgeInset = Math.max(24, window.innerWidth * 0.03);
            const mediaAspect = finalRect.width / finalRect.height;
            const maxWidthByHeight = (window.innerHeight - edgeInset * 2) * mediaAspect;
            const startWidth = Math.min(window.innerWidth - edgeInset * 2, maxWidthByHeight, 1480);
            const startCenterX = window.innerWidth / 2;
            const startCenterY = window.innerHeight / 2;
            const finalCenterX = finalRect.left + finalRect.width / 2;
            const finalCenterY = finalRect.top + finalRect.height / 2;

            sceneMeasurements.push({
                ...scene,
                startTranslateX: startCenterX - finalCenterX,
                startTranslateY: startCenterY - finalCenterY,
                startScale: startWidth / finalRect.width
            });
        }

        updateStoryScenes();
    };

    const queueStoryUpdate = () => {
        if (storyTicking) {
            return;
        }

        storyTicking = true;
        window.requestAnimationFrame(() => {
            updateStoryScenes();
            storyTicking = false;
        });
    };

    window.addEventListener("resize", measureStoryScenes, { passive: true });
    window.addEventListener("scroll", queueStoryUpdate, { passive: true });
    window.addEventListener("load", measureStoryScenes, { once: true });

    if (document.fonts?.ready) {
        document.fonts.ready.then(() => {
            measureStoryScenes();
        });
    }

    measureStoryScenes();

    if (header) {
        let lastScrollY = window.scrollY;
        let headerTicking = false;

        const updateHeader = () => {
            const currentScrollY = window.scrollY;
            const scrollingDown = currentScrollY > lastScrollY;

            if (!prefersReducedMotion && currentScrollY > 110 && scrollingDown) {
                header.classList.add("site-header--hidden");
            } else {
                header.classList.remove("site-header--hidden");
            }

            lastScrollY = currentScrollY;
            headerTicking = false;
        };

        window.addEventListener("scroll", () => {
            if (!headerTicking) {
                window.requestAnimationFrame(updateHeader);
                headerTicking = true;
            }
        }, { passive: true });

        updateHeader();
    }
});
