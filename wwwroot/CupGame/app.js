// Scene + Camera
const scene = new THREE.Scene();
scene.background = new THREE.Color(0xffffff);
const camera = new THREE.PerspectiveCamera(
    45,
    window.innerWidth / window.innerHeight,
    0.1,
    1000
);

// Renderer
const renderer = new THREE.WebGLRenderer({
    canvas: document.getElementById("gameCanvas"),
    antialias: true
});
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;

// Lights
const light1 = new THREE.DirectionalLight(0xffffff, 0.9);
light1.position.set(5, 15, 10);
light1.castShadow = true;
light1.shadow.mapSize.width = 2048;
light1.shadow.mapSize.height = 2048;
light1.shadow.camera.left = -10;
light1.shadow.camera.right = 10;
light1.shadow.camera.top = 10;
light1.shadow.camera.bottom = -10;
scene.add(light1);

const ambient = new THREE.AmbientLight(0x404040, 0.6);
scene.add(ambient);

// Cup Geometry
const cupPoints = [
    new THREE.Vector2(0, 0),
    new THREE.Vector2(1.3, 0),
    new THREE.Vector2(1.5, 2.2),
    new THREE.Vector2(1.3, 2.6),
    new THREE.Vector2(1.4, 2.8),
    new THREE.Vector2(1.2, 3.0)
];
const cupGeometry = new THREE.LatheGeometry(cupPoints, 64);

// Materials
const redBody = new THREE.MeshStandardMaterial({
    color: 0xcc0000,
    roughness: 0.4,
    metalness: 0.1
});
const whiteRim = new THREE.MeshStandardMaterial({
    color: 0xffffff,
    roughness: 0.3,
    metalness: 0.2
});

// Responsive setup function
function setupScene() {
    const screenWidth = window.innerWidth;
    const baseSpacing = 6;
    const minSpacing = 4;
    const spacing = Math.max(minSpacing, baseSpacing * Math.min(screenWidth / 800, 1));

    const fov = screenWidth < 640 ? 60 : 45;
    camera.fov = fov;
    camera.position.set(0, 10, 15 + spacing * 1.5);
    camera.lookAt(0, 2, 0);
    camera.updateProjectionMatrix();

    cups.forEach(cup => scene.remove(cup));
    cups.length = 0;

    for (let i = 0; i < 3; i++) {
        const group = new THREE.Group();
        group.name = `cup${i}`;
        const cupBody = new THREE.Mesh(cupGeometry, redBody);
        cupBody.castShadow = true;
        cupBody.receiveShadow = true;
        group.add(cupBody);
        const rimGeometry = new THREE.TorusGeometry(1.4, 0.1, 16, 64);
        const rim = new THREE.Mesh(rimGeometry, whiteRim);
        rim.rotation.x = Math.PI / 2;
        rim.position.y = 2.9;
        rim.castShadow = true;
        rim.receiveShadow = true;
        group.add(rim);
        group.scale.set(0.9, 0.9, 0.9);
        group.rotation.x = Math.PI;
        group.position.set((i - 1) * spacing, downY, 0);
        scene.add(group);
        cups.push(group);
    }

    scene.remove(ground);
    ground.geometry.dispose();
    ground.geometry = new THREE.PlaneGeometry(spacing * 10, spacing * 5);
    scene.add(ground);

    ball.position.set(cups[1].position.x, 0.7, 0);
}

// Create cups and ground
const cups = [];
const downY = 3.0;
const upY = downY + 5;
const ground = new THREE.Mesh(
    new THREE.PlaneGeometry(40, 20),
    new THREE.MeshStandardMaterial({
        color: 0x888888,
        roughness: 0.7,
        metalness: 0
    })
);
ground.rotation.x = -Math.PI / 2;
ground.position.y = 0;
ground.receiveShadow = true;

// White ball
const ballGeometry = new THREE.SphereGeometry(0.7, 32, 32);
const ballMaterial = new THREE.MeshStandardMaterial({
    color: 0xffffff,
    roughness: 0.5,
    metalness: 0
});
const ball = new THREE.Mesh(ballGeometry, ballMaterial);
ball.position.set(0, 0.7, 0);
ball.castShadow = true;
ball.receiveShadow = true;
scene.add(ball);

// Initial scene setup
setupScene();

// Animation variables
let lifting = false;
let liftStartTime = 0;
let activeCup = null;
let shuffling = false;
let shuffleStartTime = 0;
let shuffleProgress = 0;
let hasChosen = false;
let openingClosing = false;
let openCloseStartTime = 0;
let openClosePhase = 'opening';

// Audio setup
const audioListener = new THREE.AudioListener();
camera.add(audioListener);
const cupLiftSound = new THREE.Audio(audioListener);
const cupLandSound = new THREE.Audio(audioListener);
const successSound = new THREE.Audio(audioListener);
const loseSound = new THREE.Audio(audioListener);
const audioLoader = new THREE.AudioLoader();
audioLoader.load('sounds/suction.mp3', function(buffer) {
    cupLiftSound.setBuffer(buffer);
    cupLiftSound.setVolume(0.5);
});
audioLoader.load('sounds/cup-on-table.mp3', function(buffer) {
    cupLandSound.setBuffer(buffer);
    cupLandSound.setVolume(0.5);
});
audioLoader.load('sounds/success.mp3', function(buffer) {
    successSound.setBuffer(buffer);
    successSound.setVolume(0.7);
});
audioLoader.load('sounds/lose.mp3', function(buffer) {
    loseSound.setBuffer(buffer);
    loseSound.setVolume(0.6);
});

// Raycaster for mouse/touch
const raycaster = new THREE.Raycaster();
const pointer = new THREE.Vector2();

// Initialize balance
let currentBalance = 0;
function initializeBalance() {
    const balanceMeta = document.querySelector('meta[name="game-balance"]');
    currentBalance = balanceMeta ? parseInt(balanceMeta.content) : 0;
    const balanceElement = document.getElementById('balance-amount');
    balanceElement.textContent = `R${currentBalance}`;
}

// Update balance from server
function updateBalance(amount) {
    currentBalance = amount;
    document.getElementById('balance-amount').textContent = `R${amount}`;
}

// Lift all cups to show initial ball position
function openCloseCups(callback) {
    openingClosing = true;
    openCloseStartTime = performance.now();
    openClosePhase = 'opening';
    if (cupLiftSound.isPlaying) cupLiftSound.stop();
    cupLiftSound.play();

    function animateOpenClose() {
        const time = performance.now() - openCloseStartTime;
        const durationUp = 800;
        const durationPause = 600;
        const durationDown = 800;

        if (openClosePhase === 'opening') {
            const progress = Math.min(time / durationUp, 1);
            const easedProgress = 1 - Math.pow(1 - progress, 3);
            cups.forEach(cup => {
                cup.position.y = downY + easedProgress * (upY - downY);
            });
            if (progress >= 1) {
                openClosePhase = 'pausing';
                openCloseStartTime = performance.now();
            }
        } else if (openClosePhase === 'pausing') {
            if (time >= durationPause) {
                openClosePhase = 'closing';
                openCloseStartTime = performance.now();
                if (cupLandSound.isPlaying) cupLandSound.stop();
                cupLandSound.play();
            }
        } else if (openClosePhase === 'closing') {
            const progress = Math.min(time / durationDown, 1);
            const easedProgress = Math.pow(progress, 3);
            cups.forEach(cup => {
                cup.position.y = upY - easedProgress * (upY - downY);
            });
            if (progress >= 1) {
                openingClosing = false;
                cups.forEach(cup => cup.position.y = downY);
                callback();
                return;
            }
        }

        if (openingClosing) requestAnimationFrame(animateOpenClose);
    }

    requestAnimationFrame(animateOpenClose);
}

// Lift only the selected cup to reveal result
function liftSelectedCup(selectedCupIndex, callback) {
    lifting = true;
    liftStartTime = performance.now();
    let phase = 'lifting';
    if (cupLiftSound.isPlaying) cupLiftSound.stop();
    cupLiftSound.play();

    function animateLift() {
        const time = performance.now() - liftStartTime;
        const durationUp = 800;
        const durationPause = 600;
        const durationDown = 800;

        if (phase === 'lifting') {
            const progress = Math.min(time / durationUp, 1);
            const easedProgress = 1 - Math.pow(1 - progress, 3);
            cups[selectedCupIndex].position.y = downY + easedProgress * (upY - downY);
            if (progress >= 1) {
                phase = 'pausing';
                liftStartTime = performance.now();
            }
        } else if (phase === 'pausing') {
            if (time >= durationPause) {
                phase = 'lowering';
                liftStartTime = performance.now();
                if (cupLandSound.isPlaying) cupLandSound.stop();
                cupLandSound.play();
            }
        } else if (phase === 'lowering') {
            const progress = Math.min(time / durationDown, 1);
            const easedProgress = Math.pow(progress, 3);
            cups[selectedCupIndex].position.y = upY - easedProgress * (upY - downY);
            if (progress >= 1) {
                lifting = false;
                cups[selectedCupIndex].position.y = downY;
                callback();
                return;
            }
        }

        if (lifting) requestAnimationFrame(animateLift);
    }

    requestAnimationFrame(animateLift);
}

// Handle server-driven shuffle animation
function animateShuffle(data) {
    shuffling = true;
    shuffleStartTime = performance.now();
    shuffleProgress = 0;
    ball.visible = false;
    const swaps = data.swaps; // Array of [indexA, indexB]
    const initialPositions = cups.map(cup => cup.position.clone());
    if (cupLiftSound.isPlaying) cupLiftSound.stop();
    cupLiftSound.play(); // Play lift for first swap

    function performSwap() {
        if (shuffleProgress >= swaps.length) {
            shuffling = false;
            return;
        }

        const [indexA, indexB] = swaps[shuffleProgress];
        const posA = initialPositions[indexA].clone();
        const posB = initialPositions[indexB].clone();
        const swapDuration = 500;
        const time = performance.now() - shuffleStartTime;
        const progress = Math.min(time / swapDuration, 1);
        const easedProgress = 1 - Math.pow(1 - progress, 3);
        const maxLift = 1.5;
        const lift = Math.sin(easedProgress * Math.PI) * maxLift;

        cups[indexA].position.lerpVectors(posA, posB, easedProgress);
        cups[indexA].position.y = downY + lift;
        cups[indexB].position.lerpVectors(posB, posA, easedProgress);
        cups[indexB].position.y = downY + lift;

        if (progress >= 1) {
            initialPositions[indexA].copy(cups[indexA].position);
            initialPositions[indexB].copy(cups[indexB].position);
            if (cupLandSound.isPlaying) cupLandSound.stop();
            cupLandSound.play();
            shuffleProgress++;
            shuffleStartTime = performance.now();
            if (shuffleProgress < swaps.length) {
                if (cupLiftSound.isPlaying) cupLiftSound.stop();
                cupLiftSound.play();
            }
        }

        if (shuffling) requestAnimationFrame(performSwap);
    }

    requestAnimationFrame(performSwap);
}

// Handle click or touch
function onPointerDown(event) {
    if (lifting || shuffling || openingClosing || hasChosen) return;

    const clientX = event.clientX || (event.touches && event.touches[0].clientX);
    const clientY = event.clientY || (event.touches && event.touches[0].clientY);
    pointer.x = (clientX / window.innerWidth) * 2 - 1;
    pointer.y = -(clientY / window.innerHeight) * 2 + 1;

    raycaster.setFromCamera(pointer, camera);
    const intersects = raycaster.intersectObjects(cups, true);

    if (intersects.length > 0) {
        const selectedCup = intersects[0].object.parent;
        activeCup = cups.findIndex(cup => cup === selectedCup);
        if (activeCup !== -1) {
            hasChosen = true;
            // Send selection to server
            connection.invoke('SelectCup', activeCup).catch(err => console.error('Error selecting cup:', err));
        }
    }
}

// SignalR event handlers
connection.on('ShuffleCups', (data) => {
    animateShuffle(data);
});

connection.on('GameResult', (data) => {
    ball.position.set(cups[data.ballCupIndex].position.x, 0.7, 0);
    ball.visible = true;
    liftSelectedCup(activeCup, () => {
        if (data.isWin) {
            if (successSound.isPlaying) successSound.stop();
            successSound.play();
        } else {
            if (loseSound.isPlaying) loseSound.stop();
            loseSound.play();
        }
        updateBalance(data.newBalance);
        hasChosen = false;
        activeCup = null;
    });
});

connection.on('Error', (message) => {
    console.error('Server error:', message);
    alert(message);
    // Revert balance on error
    const balanceMeta = document.querySelector('meta[name="game-balance"]');
    currentBalance = balanceMeta ? parseInt(balanceMeta.content) : currentBalance;
    document.getElementById('balance-amount').textContent = `R${currentBalance}`;
});

// Add event listeners
renderer.domElement.addEventListener('mousedown', onPointerDown);
renderer.domElement.addEventListener('touchstart', onPointerDown);
document.getElementById('play-button').addEventListener('click', () => {
    if (shuffling || lifting || openingClosing || hasChosen) return;
    const betAmount = parseInt(document.getElementById('bet-select').value);
    if (betAmount > currentBalance) {
        alert('Insufficient balance!');
        return;
    }
    // Deduct bet amount immediately
    currentBalance -= betAmount;
    document.getElementById('balance-amount').textContent = `R${currentBalance}`;
    ball.position.set(cups[1].position.x, 0.7, 0); // Ensure initial middle
    openCloseCups(() => {
        connection.invoke('PlaceBet', betAmount).catch(err => console.error('Error placing bet:', err));
    });
});

// Animation loop
function animate() {
    requestAnimationFrame(animate);
    renderer.render(scene, camera);
}
animate();

// Resize handler
window.addEventListener("resize", () => {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
    setupScene();
});

// Initialize balance and start SignalR
document.addEventListener('DOMContentLoaded', () => {
    initializeBalance();
    connection.start().catch(err => console.error('SignalR Connection Error:', err));
});