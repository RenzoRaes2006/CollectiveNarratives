// De 4 keuzes uit de game flow. De key is het id dat Unity (of de host-pagina) meestuurt.
module.exports = {
  telefoon: {
    question: "De telefoon gaat. Wat doet Kevin?",
    options: [
      { id: "opnemen", label: "Opnemen" },
      { id: "rinkelen", label: "Laten rinkelen" },
    ],
  },
  hond: {
    question: "Gekrab aan de achterdeur. Het is de hond van Marley.",
    options: [
      { id: "binnen", label: "Hond binnenlaten" },
      { id: "buiten", label: "Buiten laten" },
    ],
  },
  stroom: {
    question: "De stroom valt uit. Wat doet Kevin?",
    options: [
      { id: "kelder", label: "Naar de kelder" },
      { id: "zaklamp", label: "Zaklamp zoeken" },
    ],
  },
  inbreker: {
    question: "De achterdeur gaat open. Wat doet Kevin?",
    options: [
      { id: "kast", label: "Verstoppen in de kast" },
      { id: "raam", label: "Vluchten door het raam" },
    ],
  },
};
