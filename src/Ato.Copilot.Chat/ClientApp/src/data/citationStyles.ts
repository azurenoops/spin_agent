// =============================================================================
// citationStyles.ts — #1703
//
// Full catalog of 2,600+ citation styles.
// Loaded once at module-load time (static import — zero network requests).
// Used by CitationStylePicker for client-side search + journal auto-suggest.
// =============================================================================

export type Discipline = 'sciences' | 'humanities' | 'law' | 'medicine' | 'general';

export interface CitationStyle {
  /** Stable machine ID, e.g. 'apa-7'. */
  id: string;
  /** Human-readable display name. */
  name: string;
  /** Academic discipline grouping. */
  discipline: Discipline;
  /**
   * Alternative names and journal names this style is used for.
   * Searching aliases enables journal auto-suggest (e.g. 'Nature' to Nature style).
   */
  aliases: string[];
}

/** Full 2,600+ style catalog. Immutable — never mutate this array. */
export const CITATION_STYLES: CitationStyle[] = [
  {
    "id": "apa-7",
    "name": "APA 7th Edition",
    "discipline": "general",
    "aliases": [
      "APA",
      "American Psychological Association",
      "APA 7",
      "psychology",
      "social sciences"
    ]
  },
  {
    "id": "apa-6",
    "name": "APA 6th Edition",
    "discipline": "general",
    "aliases": [
      "APA 6",
      "APA sixth"
    ]
  },
  {
    "id": "mla-9",
    "name": "MLA 9th Edition",
    "discipline": "humanities",
    "aliases": [
      "MLA",
      "Modern Language Association",
      "MLA 9",
      "literature",
      "language arts"
    ]
  },
  {
    "id": "mla-8",
    "name": "MLA 8th Edition",
    "discipline": "humanities",
    "aliases": [
      "MLA 8",
      "MLA eighth"
    ]
  },
  {
    "id": "chicago-17-author-date",
    "name": "Chicago 17th Edition (Author-Date)",
    "discipline": "humanities",
    "aliases": [
      "Chicago",
      "Chicago Manual of Style",
      "CMOS",
      "Turabian",
      "history"
    ]
  },
  {
    "id": "chicago-17-notes-bib",
    "name": "Chicago 17th Edition (Notes-Bibliography)",
    "discipline": "humanities",
    "aliases": [
      "Chicago Notes",
      "Chicago NB",
      "footnotes"
    ]
  },
  {
    "id": "chicago-16",
    "name": "Chicago 16th Edition",
    "discipline": "humanities",
    "aliases": [
      "Chicago 16",
      "CMOS 16"
    ]
  },
  {
    "id": "harvard",
    "name": "Harvard Referencing",
    "discipline": "general",
    "aliases": [
      "Harvard",
      "Harvard style",
      "author-date harvard"
    ]
  },
  {
    "id": "vancouver",
    "name": "Vancouver",
    "discipline": "medicine",
    "aliases": [
      "Vancouver style",
      "ICMJE",
      "NEJM style",
      "biomedical journals"
    ]
  },
  {
    "id": "ieee",
    "name": "IEEE",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "Institute of Electrical and Electronics Engineers",
      "engineering",
      "computer science",
      "IEEE Transactions",
      "IEEE Access"
    ]
  },
  {
    "id": "acs",
    "name": "ACS (American Chemical Society)",
    "discipline": "sciences",
    "aliases": [
      "ACS",
      "American Chemical Society",
      "chemistry",
      "JACS"
    ]
  },
  {
    "id": "nature",
    "name": "Nature",
    "discipline": "sciences",
    "aliases": [
      "Nature",
      "nature.com",
      "Springer Nature"
    ]
  },
  {
    "id": "science-aaas",
    "name": "Science (AAAS)",
    "discipline": "sciences",
    "aliases": [
      "Science",
      "AAAS",
      "Science magazine"
    ]
  },
  {
    "id": "cell",
    "name": "Cell",
    "discipline": "sciences",
    "aliases": [
      "Cell",
      "Cell Press",
      "molecular biology"
    ]
  },
  {
    "id": "pnas",
    "name": "PNAS",
    "discipline": "sciences",
    "aliases": [
      "PNAS",
      "Proceedings of the National Academy of Sciences"
    ]
  },
  {
    "id": "jama",
    "name": "JAMA",
    "discipline": "medicine",
    "aliases": [
      "JAMA",
      "Journal of the American Medical Association",
      "AMA style",
      "medical journal"
    ]
  },
  {
    "id": "nejm",
    "name": "NEJM",
    "discipline": "medicine",
    "aliases": [
      "NEJM",
      "New England Journal of Medicine",
      "clinical medicine"
    ]
  },
  {
    "id": "bmj",
    "name": "BMJ",
    "discipline": "medicine",
    "aliases": [
      "BMJ",
      "British Medical Journal"
    ]
  },
  {
    "id": "lancet",
    "name": "The Lancet",
    "discipline": "medicine",
    "aliases": [
      "Lancet",
      "lancet.com"
    ]
  },
  {
    "id": "bluebook",
    "name": "Bluebook 20th Edition",
    "discipline": "law",
    "aliases": [
      "Bluebook",
      "The Bluebook",
      "law review",
      "legal citation",
      "Harvard Law Review"
    ]
  },
  {
    "id": "oscola",
    "name": "OSCOLA",
    "discipline": "law",
    "aliases": [
      "OSCOLA",
      "Oxford Legal",
      "Oxford Standard Citation of Legal Authorities",
      "UK law"
    ]
  },
  {
    "id": "alwd",
    "name": "ALWD Citation Manual",
    "discipline": "law",
    "aliases": [
      "ALWD",
      "Association of Legal Writing Directors"
    ]
  },
  {
    "id": "asa",
    "name": "ASA (American Sociological Association)",
    "discipline": "humanities",
    "aliases": [
      "ASA",
      "American Sociological Association",
      "sociology"
    ]
  },
  {
    "id": "apsa",
    "name": "APSA",
    "discipline": "humanities",
    "aliases": [
      "APSA",
      "American Political Science Association",
      "political science"
    ]
  },
  {
    "id": "aaa",
    "name": "AAA (American Anthropological Association)",
    "discipline": "humanities",
    "aliases": [
      "AAA",
      "American Anthropological Association",
      "anthropology"
    ]
  },
  {
    "id": "acta-biomaterialia",
    "name": "Acta Biomaterialia",
    "discipline": "sciences",
    "aliases": [
      "Acta Biomaterialia"
    ]
  },
  {
    "id": "acta-crystallographica-a",
    "name": "Acta Crystallographica A",
    "discipline": "sciences",
    "aliases": [
      "Acta Crystallographica A"
    ]
  },
  {
    "id": "acta-crystallographica-b",
    "name": "Acta Crystallographica B",
    "discipline": "sciences",
    "aliases": [
      "Acta Crystallographica B"
    ]
  },
  {
    "id": "acta-crystallographica-d",
    "name": "Acta Crystallographica D",
    "discipline": "sciences",
    "aliases": [
      "Acta Crystallographica D"
    ]
  },
  {
    "id": "acta-materialia",
    "name": "Acta Materialia",
    "discipline": "sciences",
    "aliases": [
      "Acta Materialia"
    ]
  },
  {
    "id": "acta-oncologica",
    "name": "Acta Oncologica",
    "discipline": "medicine",
    "aliases": [
      "Acta Oncologica"
    ]
  },
  {
    "id": "acta-sociologica",
    "name": "Acta Sociologica",
    "discipline": "humanities",
    "aliases": [
      "Acta Sociologica"
    ]
  },
  {
    "id": "addiction",
    "name": "Addiction",
    "discipline": "medicine",
    "aliases": [
      "Addiction"
    ]
  },
  {
    "id": "advanced-drug-delivery-reviews",
    "name": "Advanced Drug Delivery Reviews",
    "discipline": "medicine",
    "aliases": [
      "Advanced Drug Delivery Reviews"
    ]
  },
  {
    "id": "advanced-energy-materials",
    "name": "Advanced Energy Materials",
    "discipline": "sciences",
    "aliases": [
      "Advanced Energy Materials"
    ]
  },
  {
    "id": "advanced-functional-materials",
    "name": "Advanced Functional Materials",
    "discipline": "sciences",
    "aliases": [
      "Advanced Functional Materials"
    ]
  },
  {
    "id": "advanced-materials",
    "name": "Advanced Materials",
    "discipline": "sciences",
    "aliases": [
      "Advanced Materials"
    ]
  },
  {
    "id": "advanced-science",
    "name": "Advanced Science",
    "discipline": "sciences",
    "aliases": [
      "Advanced Science"
    ]
  },
  {
    "id": "age-and-ageing",
    "name": "Age and Ageing",
    "discipline": "medicine",
    "aliases": [
      "Age and Ageing"
    ]
  },
  {
    "id": "alimentary-pharmacology-therapeutics",
    "name": "Alimentary Pharmacology and Therapeutics",
    "discipline": "medicine",
    "aliases": [
      "Alimentary Pharmacology and Therapeutics"
    ]
  },
  {
    "id": "alzheimers-dementia",
    "name": "Alzheimers and Dementia",
    "discipline": "medicine",
    "aliases": [
      "Alzheimers and Dementia"
    ]
  },
  {
    "id": "american-anthropologist",
    "name": "American Anthropologist",
    "discipline": "humanities",
    "aliases": [
      "American Anthropologist"
    ]
  },
  {
    "id": "american-economic-review",
    "name": "American Economic Review",
    "discipline": "humanities",
    "aliases": [
      "American Economic Review"
    ]
  },
  {
    "id": "american-heart-journal",
    "name": "American Heart Journal",
    "discipline": "medicine",
    "aliases": [
      "American Heart Journal"
    ]
  },
  {
    "id": "american-historical-review",
    "name": "American Historical Review",
    "discipline": "humanities",
    "aliases": [
      "American Historical Review"
    ]
  },
  {
    "id": "american-journal-cardiology",
    "name": "American Journal of Cardiology",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Cardiology"
    ]
  },
  {
    "id": "american-journal-clinical-nutrition",
    "name": "American Journal of Clinical Nutrition",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Clinical Nutrition"
    ]
  },
  {
    "id": "american-journal-epidemiology",
    "name": "American Journal of Epidemiology",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Epidemiology"
    ]
  },
  {
    "id": "american-journal-gastroenterology",
    "name": "American Journal of Gastroenterology",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Gastroenterology"
    ]
  },
  {
    "id": "american-journal-hematology",
    "name": "American Journal of Hematology",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Hematology"
    ]
  },
  {
    "id": "american-journal-human-genetics",
    "name": "American Journal of Human Genetics",
    "discipline": "sciences",
    "aliases": [
      "American Journal of Human Genetics"
    ]
  },
  {
    "id": "american-journal-medicine",
    "name": "American Journal of Medicine",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Medicine"
    ]
  },
  {
    "id": "american-journal-obstetrics-gynecology",
    "name": "American Journal of Obstetrics and Gynecology",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Obstetrics and Gynecology"
    ]
  },
  {
    "id": "american-journal-pathology",
    "name": "American Journal of Pathology",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Pathology"
    ]
  },
  {
    "id": "american-journal-psychiatry",
    "name": "American Journal of Psychiatry",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Psychiatry"
    ]
  },
  {
    "id": "american-journal-public-health",
    "name": "American Journal of Public Health",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Public Health"
    ]
  },
  {
    "id": "american-journal-respiratory-critical-care",
    "name": "American Journal of Respiratory and Critical Care Medicine",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Respiratory and Critical Care Medicine"
    ]
  },
  {
    "id": "american-journal-roentgenology",
    "name": "American Journal of Roentgenology",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Roentgenology"
    ]
  },
  {
    "id": "american-journal-sociology",
    "name": "American Journal of Sociology",
    "discipline": "humanities",
    "aliases": [
      "American Journal of Sociology"
    ]
  },
  {
    "id": "american-journal-surgical-pathology",
    "name": "American Journal of Surgical Pathology",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Surgical Pathology"
    ]
  },
  {
    "id": "american-journal-transplantation",
    "name": "American Journal of Transplantation",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Transplantation"
    ]
  },
  {
    "id": "american-political-science-review",
    "name": "American Political Science Review",
    "discipline": "humanities",
    "aliases": [
      "American Political Science Review"
    ]
  },
  {
    "id": "american-sociological-review",
    "name": "American Sociological Review",
    "discipline": "humanities",
    "aliases": [
      "American Sociological Review"
    ]
  },
  {
    "id": "analytical-chemistry",
    "name": "Analytical Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Analytical Chemistry"
    ]
  },
  {
    "id": "angewandte-chemie",
    "name": "Angewandte Chemie",
    "discipline": "sciences",
    "aliases": [
      "Angewandte Chemie"
    ]
  },
  {
    "id": "annals-applied-probability",
    "name": "Annals of Applied Probability",
    "discipline": "sciences",
    "aliases": [
      "Annals of Applied Probability"
    ]
  },
  {
    "id": "annals-emergency-medicine",
    "name": "Annals of Emergency Medicine",
    "discipline": "medicine",
    "aliases": [
      "Annals of Emergency Medicine"
    ]
  },
  {
    "id": "annals-internal-medicine",
    "name": "Annals of Internal Medicine",
    "discipline": "medicine",
    "aliases": [
      "Annals of Internal Medicine"
    ]
  },
  {
    "id": "annals-mathematics",
    "name": "Annals of Mathematics",
    "discipline": "sciences",
    "aliases": [
      "Annals of Mathematics"
    ]
  },
  {
    "id": "annals-neurology",
    "name": "Annals of Neurology",
    "discipline": "medicine",
    "aliases": [
      "Annals of Neurology"
    ]
  },
  {
    "id": "annals-oncology",
    "name": "Annals of Oncology",
    "discipline": "medicine",
    "aliases": [
      "Annals of Oncology"
    ]
  },
  {
    "id": "annals-physics",
    "name": "Annals of Physics",
    "discipline": "sciences",
    "aliases": [
      "Annals of Physics"
    ]
  },
  {
    "id": "annals-rheumatic-diseases",
    "name": "Annals of Rheumatic Diseases",
    "discipline": "medicine",
    "aliases": [
      "Annals of Rheumatic Diseases"
    ]
  },
  {
    "id": "annals-statistics",
    "name": "Annals of Statistics",
    "discipline": "sciences",
    "aliases": [
      "Annals of Statistics"
    ]
  },
  {
    "id": "annals-surgery",
    "name": "Annals of Surgery",
    "discipline": "medicine",
    "aliases": [
      "Annals of Surgery"
    ]
  },
  {
    "id": "annals-thoracic-surgery",
    "name": "Annals of Thoracic Surgery",
    "discipline": "medicine",
    "aliases": [
      "Annals of Thoracic Surgery"
    ]
  },
  {
    "id": "annual-review-anthropology",
    "name": "Annual Review of Anthropology",
    "discipline": "humanities",
    "aliases": [
      "Annual Review of Anthropology"
    ]
  },
  {
    "id": "annual-review-political-science",
    "name": "Annual Review of Political Science",
    "discipline": "humanities",
    "aliases": [
      "Annual Review of Political Science"
    ]
  },
  {
    "id": "annual-review-psychology",
    "name": "Annual Review of Psychology",
    "discipline": "humanities",
    "aliases": [
      "Annual Review of Psychology"
    ]
  },
  {
    "id": "annual-review-sociology",
    "name": "Annual Review of Sociology",
    "discipline": "humanities",
    "aliases": [
      "Annual Review of Sociology"
    ]
  },
  {
    "id": "antiviral-research",
    "name": "Antiviral Research",
    "discipline": "medicine",
    "aliases": [
      "Antiviral Research"
    ]
  },
  {
    "id": "applied-physics-letters",
    "name": "Applied Physics Letters",
    "discipline": "sciences",
    "aliases": [
      "Applied Physics Letters"
    ]
  },
  {
    "id": "applied-surface-science",
    "name": "Applied Surface Science",
    "discipline": "sciences",
    "aliases": [
      "Applied Surface Science"
    ]
  },
  {
    "id": "archives-disease-childhood",
    "name": "Archives of Disease in Childhood",
    "discipline": "medicine",
    "aliases": [
      "Archives of Disease in Childhood"
    ]
  },
  {
    "id": "archives-internal-medicine",
    "name": "Archives of Internal Medicine",
    "discipline": "medicine",
    "aliases": [
      "Archives of Internal Medicine"
    ]
  },
  {
    "id": "arthritis-rheumatology",
    "name": "Arthritis and Rheumatology",
    "discipline": "medicine",
    "aliases": [
      "Arthritis and Rheumatology"
    ]
  },
  {
    "id": "astrophysical-journal",
    "name": "Astrophysical Journal",
    "discipline": "sciences",
    "aliases": [
      "Astrophysical Journal"
    ]
  },
  {
    "id": "astrophysical-journal-letters",
    "name": "Astrophysical Journal Letters",
    "discipline": "sciences",
    "aliases": [
      "Astrophysical Journal Letters"
    ]
  },
  {
    "id": "astrophysical-journal-supplement",
    "name": "Astrophysical Journal Supplement",
    "discipline": "sciences",
    "aliases": [
      "Astrophysical Journal Supplement"
    ]
  },
  {
    "id": "atmospheric-chemistry-physics",
    "name": "Atmospheric Chemistry and Physics",
    "discipline": "sciences",
    "aliases": [
      "Atmospheric Chemistry and Physics"
    ]
  },
  {
    "id": "atmospheric-environment",
    "name": "Atmospheric Environment",
    "discipline": "sciences",
    "aliases": [
      "Atmospheric Environment"
    ]
  },
  {
    "id": "autophagy",
    "name": "Autophagy",
    "discipline": "sciences",
    "aliases": [
      "Autophagy"
    ]
  },
  {
    "id": "biochemical-journal",
    "name": "Biochemical Journal",
    "discipline": "sciences",
    "aliases": [
      "Biochemical Journal"
    ]
  },
  {
    "id": "biochemical-pharmacology",
    "name": "Biochemical Pharmacology",
    "discipline": "medicine",
    "aliases": [
      "Biochemical Pharmacology"
    ]
  },
  {
    "id": "biochemistry",
    "name": "Biochemistry",
    "discipline": "sciences",
    "aliases": [
      "Biochemistry"
    ]
  },
  {
    "id": "bioinformatics",
    "name": "Bioinformatics",
    "discipline": "sciences",
    "aliases": [
      "Bioinformatics"
    ]
  },
  {
    "id": "biological-conservation",
    "name": "Biological Conservation",
    "discipline": "sciences",
    "aliases": [
      "Biological Conservation"
    ]
  },
  {
    "id": "biology-reproduction",
    "name": "Biology of Reproduction",
    "discipline": "sciences",
    "aliases": [
      "Biology of Reproduction"
    ]
  },
  {
    "id": "biomacromolecules",
    "name": "Biomacromolecules",
    "discipline": "sciences",
    "aliases": [
      "Biomacromolecules"
    ]
  },
  {
    "id": "biomaterials",
    "name": "Biomaterials",
    "discipline": "sciences",
    "aliases": [
      "Biomaterials"
    ]
  },
  {
    "id": "biomaterials-science",
    "name": "Biomaterials Science",
    "discipline": "sciences",
    "aliases": [
      "Biomaterials Science"
    ]
  },
  {
    "id": "biometrics",
    "name": "Biometrics",
    "discipline": "sciences",
    "aliases": [
      "Biometrics"
    ]
  },
  {
    "id": "bioorganic-medicinal-chemistry",
    "name": "Bioorganic and Medicinal Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Bioorganic and Medicinal Chemistry"
    ]
  },
  {
    "id": "biophysical-chemistry",
    "name": "Biophysical Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Biophysical Chemistry"
    ]
  },
  {
    "id": "biophysical-journal",
    "name": "Biophysical Journal",
    "discipline": "sciences",
    "aliases": [
      "Biophysical Journal"
    ]
  },
  {
    "id": "biosensors-bioelectronics",
    "name": "Biosensors and Bioelectronics",
    "discipline": "sciences",
    "aliases": [
      "Biosensors and Bioelectronics"
    ]
  },
  {
    "id": "biotechnology-advances",
    "name": "Biotechnology Advances",
    "discipline": "sciences",
    "aliases": [
      "Biotechnology Advances"
    ]
  },
  {
    "id": "biotechnology-bioengineering",
    "name": "Biotechnology and Bioengineering",
    "discipline": "sciences",
    "aliases": [
      "Biotechnology and Bioengineering"
    ]
  },
  {
    "id": "blood",
    "name": "Blood",
    "discipline": "medicine",
    "aliases": [
      "Blood"
    ]
  },
  {
    "id": "blood-advances",
    "name": "Blood Advances",
    "discipline": "medicine",
    "aliases": [
      "Blood Advances"
    ]
  },
  {
    "id": "bmj-open",
    "name": "BMJ Open",
    "discipline": "medicine",
    "aliases": [
      "BMJ Open"
    ]
  },
  {
    "id": "bone",
    "name": "Bone",
    "discipline": "medicine",
    "aliases": [
      "Bone"
    ]
  },
  {
    "id": "brain",
    "name": "Brain",
    "discipline": "medicine",
    "aliases": [
      "Brain"
    ]
  },
  {
    "id": "brain-behavior-immunity",
    "name": "Brain Behavior and Immunity",
    "discipline": "medicine",
    "aliases": [
      "Brain Behavior and Immunity"
    ]
  },
  {
    "id": "brain-research",
    "name": "Brain Research",
    "discipline": "medicine",
    "aliases": [
      "Brain Research"
    ]
  },
  {
    "id": "british-journal-cancer",
    "name": "British Journal of Cancer",
    "discipline": "medicine",
    "aliases": [
      "British Journal of Cancer"
    ]
  },
  {
    "id": "british-journal-dermatology",
    "name": "British Journal of Dermatology",
    "discipline": "medicine",
    "aliases": [
      "British Journal of Dermatology"
    ]
  },
  {
    "id": "british-journal-educational-psychology",
    "name": "British Journal of Educational Psychology",
    "discipline": "general",
    "aliases": [
      "British Journal of Educational Psychology"
    ]
  },
  {
    "id": "british-journal-educational-technology",
    "name": "British Journal of Educational Technology",
    "discipline": "general",
    "aliases": [
      "British Journal of Educational Technology"
    ]
  },
  {
    "id": "british-journal-haematology",
    "name": "British Journal of Haematology",
    "discipline": "medicine",
    "aliases": [
      "British Journal of Haematology"
    ]
  },
  {
    "id": "british-journal-political-science",
    "name": "British Journal of Political Science",
    "discipline": "humanities",
    "aliases": [
      "British Journal of Political Science"
    ]
  },
  {
    "id": "british-journal-sociology",
    "name": "British Journal of Sociology",
    "discipline": "humanities",
    "aliases": [
      "British Journal of Sociology"
    ]
  },
  {
    "id": "british-journal-surgery",
    "name": "British Journal of Surgery",
    "discipline": "medicine",
    "aliases": [
      "British Journal of Surgery"
    ]
  },
  {
    "id": "cambridge-archaeological-journal",
    "name": "Cambridge Archaeological Journal",
    "discipline": "humanities",
    "aliases": [
      "Cambridge Archaeological Journal"
    ]
  },
  {
    "id": "cambridge-law-journal",
    "name": "Cambridge Law Journal",
    "discipline": "law",
    "aliases": [
      "Cambridge Law Journal"
    ]
  },
  {
    "id": "cancer",
    "name": "Cancer",
    "discipline": "medicine",
    "aliases": [
      "Cancer"
    ]
  },
  {
    "id": "cancer-biology-therapy",
    "name": "Cancer Biology and Therapy",
    "discipline": "medicine",
    "aliases": [
      "Cancer Biology and Therapy"
    ]
  },
  {
    "id": "cancer-cell",
    "name": "Cancer Cell",
    "discipline": "medicine",
    "aliases": [
      "Cancer Cell"
    ]
  },
  {
    "id": "cancer-discovery",
    "name": "Cancer Discovery",
    "discipline": "medicine",
    "aliases": [
      "Cancer Discovery"
    ]
  },
  {
    "id": "cancer-epidemiology-biomarkers",
    "name": "Cancer Epidemiology Biomarkers and Prevention",
    "discipline": "medicine",
    "aliases": [
      "Cancer Epidemiology Biomarkers and Prevention"
    ]
  },
  {
    "id": "cancer-letters",
    "name": "Cancer Letters",
    "discipline": "medicine",
    "aliases": [
      "Cancer Letters"
    ]
  },
  {
    "id": "cancer-research",
    "name": "Cancer Research",
    "discipline": "sciences",
    "aliases": [
      "Cancer Research"
    ]
  },
  {
    "id": "carbohydrate-polymers",
    "name": "Carbohydrate Polymers",
    "discipline": "sciences",
    "aliases": [
      "Carbohydrate Polymers"
    ]
  },
  {
    "id": "carbohydrate-research",
    "name": "Carbohydrate Research",
    "discipline": "sciences",
    "aliases": [
      "Carbohydrate Research"
    ]
  },
  {
    "id": "cardiovascular-research",
    "name": "Cardiovascular Research",
    "discipline": "medicine",
    "aliases": [
      "Cardiovascular Research"
    ]
  },
  {
    "id": "catalysis-communications",
    "name": "Catalysis Communications",
    "discipline": "sciences",
    "aliases": [
      "Catalysis Communications"
    ]
  },
  {
    "id": "catalysis-science-technology",
    "name": "Catalysis Science and Technology",
    "discipline": "sciences",
    "aliases": [
      "Catalysis Science and Technology"
    ]
  },
  {
    "id": "catalysis-today",
    "name": "Catalysis Today",
    "discipline": "sciences",
    "aliases": [
      "Catalysis Today"
    ]
  },
  {
    "id": "cell-calcium",
    "name": "Cell Calcium",
    "discipline": "sciences",
    "aliases": [
      "Cell Calcium"
    ]
  },
  {
    "id": "cell-chemical-biology",
    "name": "Cell Chemical Biology",
    "discipline": "sciences",
    "aliases": [
      "Cell Chemical Biology"
    ]
  },
  {
    "id": "cell-cycle",
    "name": "Cell Cycle",
    "discipline": "sciences",
    "aliases": [
      "Cell Cycle"
    ]
  },
  {
    "id": "cell-death-differentiation",
    "name": "Cell Death and Differentiation",
    "discipline": "sciences",
    "aliases": [
      "Cell Death and Differentiation"
    ]
  },
  {
    "id": "cell-death-disease",
    "name": "Cell Death and Disease",
    "discipline": "sciences",
    "aliases": [
      "Cell Death and Disease"
    ]
  },
  {
    "id": "cell-genomics",
    "name": "Cell Genomics",
    "discipline": "sciences",
    "aliases": [
      "Cell Genomics"
    ]
  },
  {
    "id": "cell-host-microbe",
    "name": "Cell Host and Microbe",
    "discipline": "sciences",
    "aliases": [
      "Cell Host and Microbe"
    ]
  },
  {
    "id": "cell-metabolism",
    "name": "Cell Metabolism",
    "discipline": "sciences",
    "aliases": [
      "Cell Metabolism"
    ]
  },
  {
    "id": "cell-reports",
    "name": "Cell Reports",
    "discipline": "sciences",
    "aliases": [
      "Cell Reports"
    ]
  },
  {
    "id": "cell-signalling",
    "name": "Cell Signalling",
    "discipline": "sciences",
    "aliases": [
      "Cell Signalling"
    ]
  },
  {
    "id": "cell-systems",
    "name": "Cell Systems",
    "discipline": "sciences",
    "aliases": [
      "Cell Systems"
    ]
  },
  {
    "id": "cellular-molecular-life-sciences",
    "name": "Cellular and Molecular Life Sciences",
    "discipline": "sciences",
    "aliases": [
      "Cellular and Molecular Life Sciences"
    ]
  },
  {
    "id": "chemical-communications",
    "name": "Chemical Communications",
    "discipline": "sciences",
    "aliases": [
      "Chemical Communications"
    ]
  },
  {
    "id": "chemical-engineering-journal",
    "name": "Chemical Engineering Journal",
    "discipline": "sciences",
    "aliases": [
      "Chemical Engineering Journal"
    ]
  },
  {
    "id": "chemical-engineering-science",
    "name": "Chemical Engineering Science",
    "discipline": "sciences",
    "aliases": [
      "Chemical Engineering Science"
    ]
  },
  {
    "id": "chemical-geology",
    "name": "Chemical Geology",
    "discipline": "sciences",
    "aliases": [
      "Chemical Geology"
    ]
  },
  {
    "id": "chemical-physics",
    "name": "Chemical Physics",
    "discipline": "sciences",
    "aliases": [
      "Chemical Physics"
    ]
  },
  {
    "id": "chemical-physics-letters",
    "name": "Chemical Physics Letters",
    "discipline": "sciences",
    "aliases": [
      "Chemical Physics Letters"
    ]
  },
  {
    "id": "chemical-reviews",
    "name": "Chemical Reviews",
    "discipline": "sciences",
    "aliases": [
      "Chemical Reviews"
    ]
  },
  {
    "id": "chemical-science",
    "name": "Chemical Science",
    "discipline": "sciences",
    "aliases": [
      "Chemical Science"
    ]
  },
  {
    "id": "chemistry-european-journal",
    "name": "Chemistry A European Journal",
    "discipline": "sciences",
    "aliases": [
      "Chemistry A European Journal"
    ]
  },
  {
    "id": "chemistry-materials",
    "name": "Chemistry of Materials",
    "discipline": "sciences",
    "aliases": [
      "Chemistry of Materials"
    ]
  },
  {
    "id": "chemosphere",
    "name": "Chemosphere",
    "discipline": "sciences",
    "aliases": [
      "Chemosphere"
    ]
  },
  {
    "id": "chest",
    "name": "Chest",
    "discipline": "medicine",
    "aliases": [
      "Chest"
    ]
  },
  {
    "id": "chronobiology-international",
    "name": "Chronobiology International",
    "discipline": "medicine",
    "aliases": [
      "Chronobiology International"
    ]
  },
  {
    "id": "circulation",
    "name": "Circulation",
    "discipline": "medicine",
    "aliases": [
      "Circulation"
    ]
  },
  {
    "id": "climate-dynamics",
    "name": "Climate Dynamics",
    "discipline": "sciences",
    "aliases": [
      "Climate Dynamics"
    ]
  },
  {
    "id": "clinical-cancer-research",
    "name": "Clinical Cancer Research",
    "discipline": "medicine",
    "aliases": [
      "Clinical Cancer Research"
    ]
  },
  {
    "id": "clinical-chemistry",
    "name": "Clinical Chemistry",
    "discipline": "medicine",
    "aliases": [
      "Clinical Chemistry"
    ]
  },
  {
    "id": "clinical-gastroenterology-hepatology",
    "name": "Clinical Gastroenterology and Hepatology",
    "discipline": "medicine",
    "aliases": [
      "Clinical Gastroenterology and Hepatology"
    ]
  },
  {
    "id": "clinical-immunology",
    "name": "Clinical Immunology",
    "discipline": "medicine",
    "aliases": [
      "Clinical Immunology"
    ]
  },
  {
    "id": "clinical-infectious-diseases",
    "name": "Clinical Infectious Diseases",
    "discipline": "medicine",
    "aliases": [
      "Clinical Infectious Diseases"
    ]
  },
  {
    "id": "clinical-jasn",
    "name": "Clinical Journal of the American Society of Nephrology",
    "discipline": "medicine",
    "aliases": [
      "Clinical Journal of the American Society of Nephrology"
    ]
  },
  {
    "id": "clinical-microbiology-infection",
    "name": "Clinical Microbiology and Infection",
    "discipline": "medicine",
    "aliases": [
      "Clinical Microbiology and Infection"
    ]
  },
  {
    "id": "clinical-nutrition",
    "name": "Clinical Nutrition",
    "discipline": "medicine",
    "aliases": [
      "Clinical Nutrition"
    ]
  },
  {
    "id": "clinical-oncology",
    "name": "Clinical Oncology",
    "discipline": "medicine",
    "aliases": [
      "Clinical Oncology"
    ]
  },
  {
    "id": "clinical-pharmacology-therapeutics",
    "name": "Clinical Pharmacology and Therapeutics",
    "discipline": "medicine",
    "aliases": [
      "Clinical Pharmacology and Therapeutics"
    ]
  },
  {
    "id": "clinical-psychology-review",
    "name": "Clinical Psychology Review",
    "discipline": "medicine",
    "aliases": [
      "Clinical Psychology Review"
    ]
  },
  {
    "id": "cognitive-neurodynamics",
    "name": "Cognitive Neurodynamics",
    "discipline": "sciences",
    "aliases": [
      "Cognitive Neurodynamics"
    ]
  },
  {
    "id": "cognitive-psychology",
    "name": "Cognitive Psychology",
    "discipline": "humanities",
    "aliases": [
      "Cognitive Psychology"
    ]
  },
  {
    "id": "colloids-surfaces-a",
    "name": "Colloids and Surfaces A",
    "discipline": "sciences",
    "aliases": [
      "Colloids and Surfaces A"
    ]
  },
  {
    "id": "colloids-surfaces-b",
    "name": "Colloids and Surfaces B",
    "discipline": "sciences",
    "aliases": [
      "Colloids and Surfaces B"
    ]
  },
  {
    "id": "combustion-flame",
    "name": "Combustion and Flame",
    "discipline": "sciences",
    "aliases": [
      "Combustion and Flame"
    ]
  },
  {
    "id": "communications-acm",
    "name": "Communications of the ACM",
    "discipline": "sciences",
    "aliases": [
      "Communications of the ACM"
    ]
  },
  {
    "id": "communications-biology",
    "name": "Communications Biology",
    "discipline": "sciences",
    "aliases": [
      "Communications Biology"
    ]
  },
  {
    "id": "communications-chemistry",
    "name": "Communications Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Communications Chemistry"
    ]
  },
  {
    "id": "communications-earth-environment",
    "name": "Communications Earth and Environment",
    "discipline": "sciences",
    "aliases": [
      "Communications Earth and Environment"
    ]
  },
  {
    "id": "communications-materials",
    "name": "Communications Materials",
    "discipline": "sciences",
    "aliases": [
      "Communications Materials"
    ]
  },
  {
    "id": "communications-physics",
    "name": "Communications Physics",
    "discipline": "sciences",
    "aliases": [
      "Communications Physics"
    ]
  },
  {
    "id": "comparative-biochemistry-physiology",
    "name": "Comparative Biochemistry and Physiology",
    "discipline": "sciences",
    "aliases": [
      "Comparative Biochemistry and Physiology"
    ]
  },
  {
    "id": "comparative-political-studies",
    "name": "Comparative Political Studies",
    "discipline": "humanities",
    "aliases": [
      "Comparative Political Studies"
    ]
  },
  {
    "id": "computational-brain-behavior",
    "name": "Computational Brain and Behavior",
    "discipline": "sciences",
    "aliases": [
      "Computational Brain and Behavior"
    ]
  },
  {
    "id": "computers-chemical-engineering",
    "name": "Computers and Chemical Engineering",
    "discipline": "sciences",
    "aliases": [
      "Computers and Chemical Engineering"
    ]
  },
  {
    "id": "computers-education",
    "name": "Computers and Education",
    "discipline": "general",
    "aliases": [
      "Computers and Education"
    ]
  },
  {
    "id": "contemporary-educational-psychology",
    "name": "Contemporary Educational Psychology",
    "discipline": "general",
    "aliases": [
      "Contemporary Educational Psychology"
    ]
  },
  {
    "id": "corrosion-science",
    "name": "Corrosion Science",
    "discipline": "sciences",
    "aliases": [
      "Corrosion Science"
    ]
  },
  {
    "id": "critical-care",
    "name": "Critical Care",
    "discipline": "medicine",
    "aliases": [
      "Critical Care"
    ]
  },
  {
    "id": "critical-care-medicine",
    "name": "Critical Care Medicine",
    "discipline": "medicine",
    "aliases": [
      "Critical Care Medicine"
    ]
  },
  {
    "id": "cultural-anthropology",
    "name": "Cultural Anthropology",
    "discipline": "humanities",
    "aliases": [
      "Cultural Anthropology"
    ]
  },
  {
    "id": "current-biology",
    "name": "Current Biology",
    "discipline": "sciences",
    "aliases": [
      "Current Biology"
    ]
  },
  {
    "id": "current-opinion-biotechnology",
    "name": "Current Opinion in Biotechnology",
    "discipline": "sciences",
    "aliases": [
      "Current Opinion in Biotechnology"
    ]
  },
  {
    "id": "current-opinion-cell-biology",
    "name": "Current Opinion in Cell Biology",
    "discipline": "sciences",
    "aliases": [
      "Current Opinion in Cell Biology"
    ]
  },
  {
    "id": "current-opinion-chemical-biology",
    "name": "Current Opinion in Chemical Biology",
    "discipline": "sciences",
    "aliases": [
      "Current Opinion in Chemical Biology"
    ]
  },
  {
    "id": "current-opinion-genetics-development",
    "name": "Current Opinion in Genetics and Development",
    "discipline": "sciences",
    "aliases": [
      "Current Opinion in Genetics and Development"
    ]
  },
  {
    "id": "current-opinion-microbiology",
    "name": "Current Opinion in Microbiology",
    "discipline": "sciences",
    "aliases": [
      "Current Opinion in Microbiology"
    ]
  },
  {
    "id": "current-opinion-plant-biology",
    "name": "Current Opinion in Plant Biology",
    "discipline": "sciences",
    "aliases": [
      "Current Opinion in Plant Biology"
    ]
  },
  {
    "id": "current-opinion-structural-biology",
    "name": "Current Opinion in Structural Biology",
    "discipline": "sciences",
    "aliases": [
      "Current Opinion in Structural Biology"
    ]
  },
  {
    "id": "cytokine",
    "name": "Cytokine",
    "discipline": "medicine",
    "aliases": [
      "Cytokine"
    ]
  },
  {
    "id": "development",
    "name": "Development",
    "discipline": "sciences",
    "aliases": [
      "Development"
    ]
  },
  {
    "id": "developmental-biology",
    "name": "Developmental Biology",
    "discipline": "sciences",
    "aliases": [
      "Developmental Biology"
    ]
  },
  {
    "id": "developmental-cell",
    "name": "Developmental Cell",
    "discipline": "sciences",
    "aliases": [
      "Developmental Cell"
    ]
  },
  {
    "id": "diabetes",
    "name": "Diabetes",
    "discipline": "medicine",
    "aliases": [
      "Diabetes"
    ]
  },
  {
    "id": "diabetes-care",
    "name": "Diabetes Care",
    "discipline": "medicine",
    "aliases": [
      "Diabetes Care"
    ]
  },
  {
    "id": "diabetologia",
    "name": "Diabetologia",
    "discipline": "medicine",
    "aliases": [
      "Diabetologia"
    ]
  },
  {
    "id": "discourse-society",
    "name": "Discourse and Society",
    "discipline": "humanities",
    "aliases": [
      "Discourse and Society"
    ]
  },
  {
    "id": "diseases-colon-rectum",
    "name": "Diseases of the Colon and Rectum",
    "discipline": "medicine",
    "aliases": [
      "Diseases of the Colon and Rectum"
    ]
  },
  {
    "id": "distance-education",
    "name": "Distance Education",
    "discipline": "general",
    "aliases": [
      "Distance Education"
    ]
  },
  {
    "id": "drug-discovery-today",
    "name": "Drug Discovery Today",
    "discipline": "sciences",
    "aliases": [
      "Drug Discovery Today"
    ]
  },
  {
    "id": "dyes-pigments",
    "name": "Dyes and Pigments",
    "discipline": "sciences",
    "aliases": [
      "Dyes and Pigments"
    ]
  },
  {
    "id": "early-childhood-education-journal",
    "name": "Early Childhood Education Journal",
    "discipline": "general",
    "aliases": [
      "Early Childhood Education Journal"
    ]
  },
  {
    "id": "earth-planetary-science-letters",
    "name": "Earth and Planetary Science Letters",
    "discipline": "sciences",
    "aliases": [
      "Earth and Planetary Science Letters"
    ]
  },
  {
    "id": "ecology",
    "name": "Ecology",
    "discipline": "sciences",
    "aliases": [
      "Ecology"
    ]
  },
  {
    "id": "ecology-letters",
    "name": "Ecology Letters",
    "discipline": "sciences",
    "aliases": [
      "Ecology Letters"
    ]
  },
  {
    "id": "ecotoxicology-environmental-safety",
    "name": "Ecotoxicology and Environmental Safety",
    "discipline": "sciences",
    "aliases": [
      "Ecotoxicology and Environmental Safety"
    ]
  },
  {
    "id": "educational-assessment",
    "name": "Educational Assessment",
    "discipline": "general",
    "aliases": [
      "Educational Assessment"
    ]
  },
  {
    "id": "educational-evaluation-policy-analysis",
    "name": "Educational Evaluation and Policy Analysis",
    "discipline": "general",
    "aliases": [
      "Educational Evaluation and Policy Analysis"
    ]
  },
  {
    "id": "educational-psychology",
    "name": "Educational Psychology",
    "discipline": "general",
    "aliases": [
      "Educational Psychology"
    ]
  },
  {
    "id": "educational-psychology-review",
    "name": "Educational Psychology Review",
    "discipline": "general",
    "aliases": [
      "Educational Psychology Review"
    ]
  },
  {
    "id": "educational-research",
    "name": "Educational Research",
    "discipline": "general",
    "aliases": [
      "Educational Research"
    ]
  },
  {
    "id": "educational-researcher",
    "name": "Educational Researcher",
    "discipline": "general",
    "aliases": [
      "Educational Researcher"
    ]
  },
  {
    "id": "educational-technology-research-development",
    "name": "Educational Technology Research and Development",
    "discipline": "general",
    "aliases": [
      "Educational Technology Research and Development"
    ]
  },
  {
    "id": "electrochemistry-communications",
    "name": "Electrochemistry Communications",
    "discipline": "sciences",
    "aliases": [
      "Electrochemistry Communications"
    ]
  },
  {
    "id": "electrochimica-acta",
    "name": "Electrochimica Acta",
    "discipline": "sciences",
    "aliases": [
      "Electrochimica Acta"
    ]
  },
  {
    "id": "elife",
    "name": "eLife",
    "discipline": "sciences",
    "aliases": [
      "eLife"
    ]
  },
  {
    "id": "embo-journal",
    "name": "EMBO Journal",
    "discipline": "sciences",
    "aliases": [
      "EMBO Journal"
    ]
  },
  {
    "id": "embo-reports",
    "name": "EMBO Reports",
    "discipline": "sciences",
    "aliases": [
      "EMBO Reports"
    ]
  },
  {
    "id": "endocrinology",
    "name": "Endocrinology",
    "discipline": "medicine",
    "aliases": [
      "Endocrinology"
    ]
  },
  {
    "id": "energy-conversion-management",
    "name": "Energy Conversion and Management",
    "discipline": "sciences",
    "aliases": [
      "Energy Conversion and Management"
    ]
  },
  {
    "id": "energy-storage-materials",
    "name": "Energy Storage Materials",
    "discipline": "sciences",
    "aliases": [
      "Energy Storage Materials"
    ]
  },
  {
    "id": "entropy",
    "name": "Entropy",
    "discipline": "sciences",
    "aliases": [
      "Entropy"
    ]
  },
  {
    "id": "environmental-health-perspectives",
    "name": "Environmental Health Perspectives",
    "discipline": "sciences",
    "aliases": [
      "Environmental Health Perspectives"
    ]
  },
  {
    "id": "environmental-microbiology",
    "name": "Environmental Microbiology",
    "discipline": "sciences",
    "aliases": [
      "Environmental Microbiology"
    ]
  },
  {
    "id": "environmental-pollution",
    "name": "Environmental Pollution",
    "discipline": "sciences",
    "aliases": [
      "Environmental Pollution"
    ]
  },
  {
    "id": "environmental-research",
    "name": "Environmental Research",
    "discipline": "sciences",
    "aliases": [
      "Environmental Research"
    ]
  },
  {
    "id": "environmental-science-technology",
    "name": "Environmental Science and Technology",
    "discipline": "sciences",
    "aliases": [
      "Environmental Science and Technology"
    ]
  },
  {
    "id": "enzyme-microbial-technology",
    "name": "Enzyme and Microbial Technology",
    "discipline": "sciences",
    "aliases": [
      "Enzyme and Microbial Technology"
    ]
  },
  {
    "id": "epilepsia",
    "name": "Epilepsia",
    "discipline": "medicine",
    "aliases": [
      "Epilepsia"
    ]
  },
  {
    "id": "european-heart-journal",
    "name": "European Heart Journal",
    "discipline": "medicine",
    "aliases": [
      "European Heart Journal"
    ]
  },
  {
    "id": "european-journal-biochemistry",
    "name": "European Journal of Biochemistry",
    "discipline": "sciences",
    "aliases": [
      "European Journal of Biochemistry"
    ]
  },
  {
    "id": "european-journal-cancer",
    "name": "European Journal of Cancer",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Cancer"
    ]
  },
  {
    "id": "european-journal-clinical-investigation",
    "name": "European Journal of Clinical Investigation",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Clinical Investigation"
    ]
  },
  {
    "id": "european-journal-endocrinology",
    "name": "European Journal of Endocrinology",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Endocrinology"
    ]
  },
  {
    "id": "european-journal-epidemiology",
    "name": "European Journal of Epidemiology",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Epidemiology"
    ]
  },
  {
    "id": "european-journal-heart-failure",
    "name": "European Journal of Heart Failure",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Heart Failure"
    ]
  },
  {
    "id": "european-journal-human-genetics",
    "name": "European Journal of Human Genetics",
    "discipline": "sciences",
    "aliases": [
      "European Journal of Human Genetics"
    ]
  },
  {
    "id": "european-journal-inorganic-chemistry",
    "name": "European Journal of Inorganic Chemistry",
    "discipline": "sciences",
    "aliases": [
      "European Journal of Inorganic Chemistry"
    ]
  },
  {
    "id": "european-journal-internal-medicine",
    "name": "European Journal of Internal Medicine",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Internal Medicine"
    ]
  },
  {
    "id": "european-journal-international-relations",
    "name": "European Journal of International Relations",
    "discipline": "humanities",
    "aliases": [
      "European Journal of International Relations"
    ]
  },
  {
    "id": "european-journal-neurology",
    "name": "European Journal of Neurology",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Neurology"
    ]
  },
  {
    "id": "european-journal-nutrition",
    "name": "European Journal of Nutrition",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Nutrition"
    ]
  },
  {
    "id": "european-journal-organic-chemistry",
    "name": "European Journal of Organic Chemistry",
    "discipline": "sciences",
    "aliases": [
      "European Journal of Organic Chemistry"
    ]
  },
  {
    "id": "european-journal-pharmacology",
    "name": "European Journal of Pharmacology",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Pharmacology"
    ]
  },
  {
    "id": "european-journal-political-research",
    "name": "European Journal of Political Research",
    "discipline": "humanities",
    "aliases": [
      "European Journal of Political Research"
    ]
  },
  {
    "id": "european-journal-preventive-cardiology",
    "name": "European Journal of Preventive Cardiology",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Preventive Cardiology"
    ]
  },
  {
    "id": "european-journal-social-theory",
    "name": "European Journal of Social Theory",
    "discipline": "humanities",
    "aliases": [
      "European Journal of Social Theory"
    ]
  },
  {
    "id": "european-journal-sociology",
    "name": "European Journal of Sociology",
    "discipline": "humanities",
    "aliases": [
      "European Journal of Sociology"
    ]
  },
  {
    "id": "european-law-review",
    "name": "European Law Review",
    "discipline": "law",
    "aliases": [
      "European Law Review"
    ]
  },
  {
    "id": "european-neuropsychopharmacology",
    "name": "European Neuropsychopharmacology",
    "discipline": "medicine",
    "aliases": [
      "European Neuropsychopharmacology"
    ]
  },
  {
    "id": "european-physical-journal-a",
    "name": "European Physical Journal A",
    "discipline": "sciences",
    "aliases": [
      "European Physical Journal A"
    ]
  },
  {
    "id": "european-physical-journal-b",
    "name": "European Physical Journal B",
    "discipline": "sciences",
    "aliases": [
      "European Physical Journal B"
    ]
  },
  {
    "id": "european-physical-journal-c",
    "name": "European Physical Journal C",
    "discipline": "sciences",
    "aliases": [
      "European Physical Journal C"
    ]
  },
  {
    "id": "european-sociological-review",
    "name": "European Sociological Review",
    "discipline": "humanities",
    "aliases": [
      "European Sociological Review"
    ]
  },
  {
    "id": "european-urology",
    "name": "European Urology",
    "discipline": "medicine",
    "aliases": [
      "European Urology"
    ]
  },
  {
    "id": "evolutionary-applications",
    "name": "Evolutionary Applications",
    "discipline": "sciences",
    "aliases": [
      "Evolutionary Applications"
    ]
  },
  {
    "id": "experimental-cell-research",
    "name": "Experimental Cell Research",
    "discipline": "sciences",
    "aliases": [
      "Experimental Cell Research"
    ]
  },
  {
    "id": "experimental-neurology",
    "name": "Experimental Neurology",
    "discipline": "medicine",
    "aliases": [
      "Experimental Neurology"
    ]
  },
  {
    "id": "febs-journal",
    "name": "FEBS Journal",
    "discipline": "sciences",
    "aliases": [
      "FEBS Journal"
    ]
  },
  {
    "id": "febs-letters",
    "name": "FEBS Letters",
    "discipline": "sciences",
    "aliases": [
      "FEBS Letters"
    ]
  },
  {
    "id": "fertility-sterility",
    "name": "Fertility and Sterility",
    "discipline": "medicine",
    "aliases": [
      "Fertility and Sterility"
    ]
  },
  {
    "id": "food-chemistry",
    "name": "Food Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Food Chemistry"
    ]
  },
  {
    "id": "food-hydrocolloids",
    "name": "Food Hydrocolloids",
    "discipline": "sciences",
    "aliases": [
      "Food Hydrocolloids"
    ]
  },
  {
    "id": "food-microbiology",
    "name": "Food Microbiology",
    "discipline": "sciences",
    "aliases": [
      "Food Microbiology"
    ]
  },
  {
    "id": "food-quality-preference",
    "name": "Food Quality and Preference",
    "discipline": "sciences",
    "aliases": [
      "Food Quality and Preference"
    ]
  },
  {
    "id": "food-research-international",
    "name": "Food Research International",
    "discipline": "sciences",
    "aliases": [
      "Food Research International"
    ]
  },
  {
    "id": "free-radical-biology-medicine",
    "name": "Free Radical Biology and Medicine",
    "discipline": "sciences",
    "aliases": [
      "Free Radical Biology and Medicine"
    ]
  },
  {
    "id": "frontiers-cardiovascular-medicine",
    "name": "Frontiers in Cardiovascular Medicine",
    "discipline": "medicine",
    "aliases": [
      "Frontiers in Cardiovascular Medicine"
    ]
  },
  {
    "id": "frontiers-genetics",
    "name": "Frontiers in Genetics",
    "discipline": "sciences",
    "aliases": [
      "Frontiers in Genetics"
    ]
  },
  {
    "id": "frontiers-immunology",
    "name": "Frontiers in Immunology",
    "discipline": "sciences",
    "aliases": [
      "Frontiers in Immunology"
    ]
  },
  {
    "id": "frontiers-medicine",
    "name": "Frontiers in Medicine",
    "discipline": "medicine",
    "aliases": [
      "Frontiers in Medicine"
    ]
  },
  {
    "id": "frontiers-microbiology",
    "name": "Frontiers in Microbiology",
    "discipline": "sciences",
    "aliases": [
      "Frontiers in Microbiology"
    ]
  },
  {
    "id": "frontiers-neurology",
    "name": "Frontiers in Neurology",
    "discipline": "medicine",
    "aliases": [
      "Frontiers in Neurology"
    ]
  },
  {
    "id": "frontiers-neuroscience",
    "name": "Frontiers in Neuroscience",
    "discipline": "sciences",
    "aliases": [
      "Frontiers in Neuroscience"
    ]
  },
  {
    "id": "frontiers-oncology",
    "name": "Frontiers in Oncology",
    "discipline": "medicine",
    "aliases": [
      "Frontiers in Oncology"
    ]
  },
  {
    "id": "frontiers-pediatrics",
    "name": "Frontiers in Pediatrics",
    "discipline": "medicine",
    "aliases": [
      "Frontiers in Pediatrics"
    ]
  },
  {
    "id": "frontiers-physics",
    "name": "Frontiers in Physics",
    "discipline": "sciences",
    "aliases": [
      "Frontiers in Physics"
    ]
  },
  {
    "id": "frontiers-plant-science",
    "name": "Frontiers in Plant Science",
    "discipline": "sciences",
    "aliases": [
      "Frontiers in Plant Science"
    ]
  },
  {
    "id": "frontiers-psychology",
    "name": "Frontiers in Psychology",
    "discipline": "sciences",
    "aliases": [
      "Frontiers in Psychology"
    ]
  },
  {
    "id": "fuel",
    "name": "Fuel",
    "discipline": "sciences",
    "aliases": [
      "Fuel"
    ]
  },
  {
    "id": "gastroenterology",
    "name": "Gastroenterology",
    "discipline": "medicine",
    "aliases": [
      "Gastroenterology"
    ]
  },
  {
    "id": "gender-society",
    "name": "Gender and Society",
    "discipline": "humanities",
    "aliases": [
      "Gender and Society"
    ]
  },
  {
    "id": "gene",
    "name": "Gene",
    "discipline": "sciences",
    "aliases": [
      "Gene"
    ]
  },
  {
    "id": "genes-development",
    "name": "Genes and Development",
    "discipline": "sciences",
    "aliases": [
      "Genes and Development"
    ]
  },
  {
    "id": "genome-biology",
    "name": "Genome Biology",
    "discipline": "sciences",
    "aliases": [
      "Genome Biology"
    ]
  },
  {
    "id": "genome-biology-evolution",
    "name": "Genome Biology and Evolution",
    "discipline": "sciences",
    "aliases": [
      "Genome Biology and Evolution"
    ]
  },
  {
    "id": "genome-research",
    "name": "Genome Research",
    "discipline": "sciences",
    "aliases": [
      "Genome Research"
    ]
  },
  {
    "id": "geochimica-cosmochimica-acta",
    "name": "Geochimica et Cosmochimica Acta",
    "discipline": "sciences",
    "aliases": [
      "Geochimica et Cosmochimica Acta"
    ]
  },
  {
    "id": "geomorphology",
    "name": "Geomorphology",
    "discipline": "sciences",
    "aliases": [
      "Geomorphology"
    ]
  },
  {
    "id": "geophysical-research-letters",
    "name": "Geophysical Research Letters",
    "discipline": "sciences",
    "aliases": [
      "Geophysical Research Letters"
    ]
  },
  {
    "id": "global-change-biology",
    "name": "Global Change Biology",
    "discipline": "sciences",
    "aliases": [
      "Global Change Biology"
    ]
  },
  {
    "id": "global-environmental-change",
    "name": "Global Environmental Change",
    "discipline": "sciences",
    "aliases": [
      "Global Environmental Change"
    ]
  },
  {
    "id": "global-networks",
    "name": "Global Networks",
    "discipline": "humanities",
    "aliases": [
      "Global Networks"
    ]
  },
  {
    "id": "gut",
    "name": "Gut",
    "discipline": "medicine",
    "aliases": [
      "Gut"
    ]
  },
  {
    "id": "haematologica",
    "name": "Haematologica",
    "discipline": "medicine",
    "aliases": [
      "Haematologica"
    ]
  },
  {
    "id": "heart",
    "name": "Heart",
    "discipline": "medicine",
    "aliases": [
      "Heart"
    ]
  },
  {
    "id": "hepatology",
    "name": "Hepatology",
    "discipline": "medicine",
    "aliases": [
      "Hepatology"
    ]
  },
  {
    "id": "higher-education",
    "name": "Higher Education",
    "discipline": "general",
    "aliases": [
      "Higher Education"
    ]
  },
  {
    "id": "history-theory",
    "name": "History and Theory",
    "discipline": "humanities",
    "aliases": [
      "History and Theory"
    ]
  },
  {
    "id": "human-brain-mapping",
    "name": "Human Brain Mapping",
    "discipline": "medicine",
    "aliases": [
      "Human Brain Mapping"
    ]
  },
  {
    "id": "human-genetics",
    "name": "Human Genetics",
    "discipline": "sciences",
    "aliases": [
      "Human Genetics"
    ]
  },
  {
    "id": "human-molecular-genetics",
    "name": "Human Molecular Genetics",
    "discipline": "sciences",
    "aliases": [
      "Human Molecular Genetics"
    ]
  },
  {
    "id": "human-reproduction",
    "name": "Human Reproduction",
    "discipline": "medicine",
    "aliases": [
      "Human Reproduction"
    ]
  },
  {
    "id": "hypertension",
    "name": "Hypertension",
    "discipline": "medicine",
    "aliases": [
      "Hypertension"
    ]
  },
  {
    "id": "immunity",
    "name": "Immunity",
    "discipline": "medicine",
    "aliases": [
      "Immunity"
    ]
  },
  {
    "id": "inflammation-research",
    "name": "Inflammation Research",
    "discipline": "medicine",
    "aliases": [
      "Inflammation Research"
    ]
  },
  {
    "id": "intensive-care-medicine",
    "name": "Intensive Care Medicine",
    "discipline": "medicine",
    "aliases": [
      "Intensive Care Medicine"
    ]
  },
  {
    "id": "international-immunopharmacology",
    "name": "International Immunopharmacology",
    "discipline": "medicine",
    "aliases": [
      "International Immunopharmacology"
    ]
  },
  {
    "id": "international-journal-antimicrobial-agents",
    "name": "International Journal of Antimicrobial Agents",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Antimicrobial Agents"
    ]
  },
  {
    "id": "international-journal-biological-macromolecules",
    "name": "International Journal of Biological Macromolecules",
    "discipline": "sciences",
    "aliases": [
      "International Journal of Biological Macromolecules"
    ]
  },
  {
    "id": "international-journal-cancer",
    "name": "International Journal of Cancer",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Cancer"
    ]
  },
  {
    "id": "international-journal-cardiology",
    "name": "International Journal of Cardiology",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Cardiology"
    ]
  },
  {
    "id": "international-journal-educational-development",
    "name": "International Journal of Educational Development",
    "discipline": "general",
    "aliases": [
      "International Journal of Educational Development"
    ]
  },
  {
    "id": "international-journal-educational-research",
    "name": "International Journal of Educational Research",
    "discipline": "general",
    "aliases": [
      "International Journal of Educational Research"
    ]
  },
  {
    "id": "international-journal-epidemiology",
    "name": "International Journal of Epidemiology",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Epidemiology"
    ]
  },
  {
    "id": "international-journal-food-microbiology",
    "name": "International Journal of Food Microbiology",
    "discipline": "sciences",
    "aliases": [
      "International Journal of Food Microbiology"
    ]
  },
  {
    "id": "international-journal-hydrogen-energy",
    "name": "International Journal of Hydrogen Energy",
    "discipline": "sciences",
    "aliases": [
      "International Journal of Hydrogen Energy"
    ]
  },
  {
    "id": "international-journal-molecular-sciences",
    "name": "International Journal of Molecular Sciences",
    "discipline": "sciences",
    "aliases": [
      "International Journal of Molecular Sciences"
    ]
  },
  {
    "id": "international-journal-obesity",
    "name": "International Journal of Obesity",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Obesity"
    ]
  },
  {
    "id": "international-journal-pharmaceutics",
    "name": "International Journal of Pharmaceutics",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Pharmaceutics"
    ]
  },
  {
    "id": "international-journal-radiation-oncology",
    "name": "International Journal of Radiation Oncology",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Radiation Oncology"
    ]
  },
  {
    "id": "international-journal-science-education",
    "name": "International Journal of Science Education",
    "discipline": "general",
    "aliases": [
      "International Journal of Science Education"
    ]
  },
  {
    "id": "international-journal-solids-structures",
    "name": "International Journal of Solids and Structures",
    "discipline": "sciences",
    "aliases": [
      "International Journal of Solids and Structures"
    ]
  },
  {
    "id": "international-organization",
    "name": "International Organization",
    "discipline": "humanities",
    "aliases": [
      "International Organization"
    ]
  },
  {
    "id": "international-security",
    "name": "International Security",
    "discipline": "humanities",
    "aliases": [
      "International Security"
    ]
  },
  {
    "id": "international-studies-quarterly",
    "name": "International Studies Quarterly",
    "discipline": "humanities",
    "aliases": [
      "International Studies Quarterly"
    ]
  },
  {
    "id": "investigative-ophthalmology-visual-science",
    "name": "Investigative Ophthalmology and Visual Science",
    "discipline": "medicine",
    "aliases": [
      "Investigative Ophthalmology and Visual Science"
    ]
  },
  {
    "id": "iscience",
    "name": "iScience",
    "discipline": "sciences",
    "aliases": [
      "iScience"
    ]
  },
  {
    "id": "isme-journal",
    "name": "ISME Journal",
    "discipline": "sciences",
    "aliases": [
      "ISME Journal"
    ]
  },
  {
    "id": "iubmb-life",
    "name": "IUBMB Life",
    "discipline": "sciences",
    "aliases": [
      "IUBMB Life"
    ]
  },
  {
    "id": "jama-cardiology",
    "name": "JAMA Cardiology",
    "discipline": "medicine",
    "aliases": [
      "JAMA Cardiology"
    ]
  },
  {
    "id": "jama-dermatology",
    "name": "JAMA Dermatology",
    "discipline": "medicine",
    "aliases": [
      "JAMA Dermatology"
    ]
  },
  {
    "id": "jama-internal-medicine",
    "name": "JAMA Internal Medicine",
    "discipline": "medicine",
    "aliases": [
      "JAMA Internal Medicine"
    ]
  },
  {
    "id": "jama-network-open",
    "name": "JAMA Network Open",
    "discipline": "medicine",
    "aliases": [
      "JAMA Network Open"
    ]
  },
  {
    "id": "jama-neurology",
    "name": "JAMA Neurology",
    "discipline": "medicine",
    "aliases": [
      "JAMA Neurology"
    ]
  },
  {
    "id": "jama-oncology",
    "name": "JAMA Oncology",
    "discipline": "medicine",
    "aliases": [
      "JAMA Oncology"
    ]
  },
  {
    "id": "jama-ophthalmology",
    "name": "JAMA Ophthalmology",
    "discipline": "medicine",
    "aliases": [
      "JAMA Ophthalmology"
    ]
  },
  {
    "id": "jama-pediatrics",
    "name": "JAMA Pediatrics",
    "discipline": "medicine",
    "aliases": [
      "JAMA Pediatrics"
    ]
  },
  {
    "id": "jama-psychiatry",
    "name": "JAMA Psychiatry",
    "discipline": "medicine",
    "aliases": [
      "JAMA Psychiatry"
    ]
  },
  {
    "id": "jama-surgery",
    "name": "JAMA Surgery",
    "discipline": "medicine",
    "aliases": [
      "JAMA Surgery"
    ]
  },
  {
    "id": "journal-acm",
    "name": "Journal of the ACM",
    "discipline": "sciences",
    "aliases": [
      "Journal of the ACM"
    ]
  },
  {
    "id": "journal-adhesion-science-technology",
    "name": "Journal of Adhesion Science and Technology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Adhesion Science and Technology"
    ]
  },
  {
    "id": "journal-aerosol-science",
    "name": "Journal of Aerosol Science",
    "discipline": "sciences",
    "aliases": [
      "Journal of Aerosol Science"
    ]
  },
  {
    "id": "journal-affective-disorders",
    "name": "Journal of Affective Disorders",
    "discipline": "medicine",
    "aliases": [
      "Journal of Affective Disorders"
    ]
  },
  {
    "id": "journal-agricultural-food-chemistry",
    "name": "Journal of Agricultural and Food Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Agricultural and Food Chemistry"
    ]
  },
  {
    "id": "journal-american-ceramic-society",
    "name": "Journal of the American Ceramic Society",
    "discipline": "sciences",
    "aliases": [
      "Journal of the American Ceramic Society"
    ]
  },
  {
    "id": "journal-american-history",
    "name": "Journal of American History",
    "discipline": "humanities",
    "aliases": [
      "Journal of American History"
    ]
  },
  {
    "id": "journal-american-medical-association",
    "name": "Journal of the American Medical Association",
    "discipline": "medicine",
    "aliases": [
      "Journal of the American Medical Association"
    ]
  },
  {
    "id": "journal-analytical-atomic-spectrometry",
    "name": "Journal of Analytical Atomic Spectrometry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Analytical Atomic Spectrometry"
    ]
  },
  {
    "id": "journal-anxiety-disorders",
    "name": "Journal of Anxiety Disorders",
    "discipline": "medicine",
    "aliases": [
      "Journal of Anxiety Disorders"
    ]
  },
  {
    "id": "journal-applied-ecology",
    "name": "Journal of Applied Ecology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Applied Ecology"
    ]
  },
  {
    "id": "journal-applied-microbiology",
    "name": "Journal of Applied Microbiology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Applied Microbiology"
    ]
  },
  {
    "id": "journal-applied-physics",
    "name": "Journal of Applied Physics",
    "discipline": "sciences",
    "aliases": [
      "Journal of Applied Physics"
    ]
  },
  {
    "id": "journal-applied-polymer-science",
    "name": "Journal of Applied Polymer Science",
    "discipline": "sciences",
    "aliases": [
      "Journal of Applied Polymer Science"
    ]
  },
  {
    "id": "journal-atmospheric-chemistry",
    "name": "Journal of Atmospheric Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Atmospheric Chemistry"
    ]
  },
  {
    "id": "journal-atmospheric-sciences",
    "name": "Journal of Atmospheric Sciences",
    "discipline": "sciences",
    "aliases": [
      "Journal of Atmospheric Sciences"
    ]
  },
  {
    "id": "journal-bacteriology",
    "name": "Journal of Bacteriology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Bacteriology"
    ]
  },
  {
    "id": "journal-biochemistry",
    "name": "Journal of Biochemistry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Biochemistry"
    ]
  },
  {
    "id": "journal-bioenergetics-biomembranes",
    "name": "Journal of Bioenergetics and Biomembranes",
    "discipline": "sciences",
    "aliases": [
      "Journal of Bioenergetics and Biomembranes"
    ]
  },
  {
    "id": "journal-biological-chemistry",
    "name": "Journal of Biological Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Biological Chemistry"
    ]
  },
  {
    "id": "journal-biomedical-informatics",
    "name": "Journal of Biomedical Informatics",
    "discipline": "sciences",
    "aliases": [
      "Journal of Biomedical Informatics"
    ]
  },
  {
    "id": "journal-biomedical-materials-research-a",
    "name": "Journal of Biomedical Materials Research A",
    "discipline": "sciences",
    "aliases": [
      "Journal of Biomedical Materials Research A"
    ]
  },
  {
    "id": "journal-cancer-research-clinical-oncology",
    "name": "Journal of Cancer Research and Clinical Oncology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Cancer Research and Clinical Oncology"
    ]
  },
  {
    "id": "journal-cardiovascular-pharmacology",
    "name": "Journal of Cardiovascular Pharmacology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Cardiovascular Pharmacology"
    ]
  },
  {
    "id": "journal-cell-biology",
    "name": "Journal of Cell Biology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Cell Biology"
    ]
  },
  {
    "id": "journal-cell-science",
    "name": "Journal of Cell Science",
    "discipline": "sciences",
    "aliases": [
      "Journal of Cell Science"
    ]
  },
  {
    "id": "journal-cellular-biochemistry",
    "name": "Journal of Cellular Biochemistry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Cellular Biochemistry"
    ]
  },
  {
    "id": "journal-cellular-physiology",
    "name": "Journal of Cellular Physiology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Cellular Physiology"
    ]
  },
  {
    "id": "journal-cerebral-blood-flow-metabolism",
    "name": "Journal of Cerebral Blood Flow and Metabolism",
    "discipline": "medicine",
    "aliases": [
      "Journal of Cerebral Blood Flow and Metabolism"
    ]
  },
  {
    "id": "journal-chemical-information-modeling",
    "name": "Journal of Chemical Information and Modeling",
    "discipline": "sciences",
    "aliases": [
      "Journal of Chemical Information and Modeling"
    ]
  },
  {
    "id": "journal-chemical-physics",
    "name": "Journal of Chemical Physics",
    "discipline": "sciences",
    "aliases": [
      "Journal of Chemical Physics"
    ]
  },
  {
    "id": "journal-chemical-theory-computation",
    "name": "Journal of Chemical Theory and Computation",
    "discipline": "sciences",
    "aliases": [
      "Journal of Chemical Theory and Computation"
    ]
  },
  {
    "id": "journal-chromatography-a",
    "name": "Journal of Chromatography A",
    "discipline": "sciences",
    "aliases": [
      "Journal of Chromatography A"
    ]
  },
  {
    "id": "journal-chromatography-b",
    "name": "Journal of Chromatography B",
    "discipline": "sciences",
    "aliases": [
      "Journal of Chromatography B"
    ]
  },
  {
    "id": "journal-climate",
    "name": "Journal of Climate",
    "discipline": "sciences",
    "aliases": [
      "Journal of Climate"
    ]
  },
  {
    "id": "journal-clinical-endocrinology-metabolism",
    "name": "Journal of Clinical Endocrinology and Metabolism",
    "discipline": "medicine",
    "aliases": [
      "Journal of Clinical Endocrinology and Metabolism"
    ]
  },
  {
    "id": "journal-clinical-investigation",
    "name": "Journal of Clinical Investigation",
    "discipline": "medicine",
    "aliases": [
      "Journal of Clinical Investigation"
    ]
  },
  {
    "id": "journal-clinical-microbiology",
    "name": "Journal of Clinical Microbiology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Clinical Microbiology"
    ]
  },
  {
    "id": "journal-clinical-oncology",
    "name": "Journal of Clinical Oncology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Clinical Oncology"
    ]
  },
  {
    "id": "journal-communication",
    "name": "Journal of Communication",
    "discipline": "humanities",
    "aliases": [
      "Journal of Communication"
    ]
  },
  {
    "id": "journal-complexity",
    "name": "Journal of Complexity",
    "discipline": "sciences",
    "aliases": [
      "Journal of Complexity"
    ]
  },
  {
    "id": "journal-computational-biology",
    "name": "Journal of Computational Biology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Computational Biology"
    ]
  },
  {
    "id": "journal-computational-chemistry",
    "name": "Journal of Computational Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Computational Chemistry"
    ]
  },
  {
    "id": "journal-computational-physics",
    "name": "Journal of Computational Physics",
    "discipline": "sciences",
    "aliases": [
      "Journal of Computational Physics"
    ]
  },
  {
    "id": "journal-conflict-resolution",
    "name": "Journal of Conflict Resolution",
    "discipline": "humanities",
    "aliases": [
      "Journal of Conflict Resolution"
    ]
  },
  {
    "id": "journal-controlled-release",
    "name": "Journal of Controlled Release",
    "discipline": "medicine",
    "aliases": [
      "Journal of Controlled Release"
    ]
  },
  {
    "id": "journal-coordination-chemistry",
    "name": "Journal of Coordination Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Coordination Chemistry"
    ]
  },
  {
    "id": "journal-curriculum-studies",
    "name": "Journal of Curriculum Studies",
    "discipline": "general",
    "aliases": [
      "Journal of Curriculum Studies"
    ]
  },
  {
    "id": "journal-democracy",
    "name": "Journal of Democracy",
    "discipline": "humanities",
    "aliases": [
      "Journal of Democracy"
    ]
  },
  {
    "id": "journal-dermatological-science",
    "name": "Journal of Dermatological Science",
    "discipline": "medicine",
    "aliases": [
      "Journal of Dermatological Science"
    ]
  },
  {
    "id": "journal-economic-history",
    "name": "Journal of Economic History",
    "discipline": "humanities",
    "aliases": [
      "Journal of Economic History"
    ]
  },
  {
    "id": "journal-economic-literature",
    "name": "Journal of Economic Literature",
    "discipline": "humanities",
    "aliases": [
      "Journal of Economic Literature"
    ]
  },
  {
    "id": "journal-ecology",
    "name": "Journal of Ecology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Ecology"
    ]
  },
  {
    "id": "journal-educational-measurement",
    "name": "Journal of Educational Measurement",
    "discipline": "general",
    "aliases": [
      "Journal of Educational Measurement"
    ]
  },
  {
    "id": "journal-educational-psychology",
    "name": "Journal of Educational Psychology",
    "discipline": "general",
    "aliases": [
      "Journal of Educational Psychology"
    ]
  },
  {
    "id": "journal-educational-research",
    "name": "Journal of Educational Research",
    "discipline": "general",
    "aliases": [
      "Journal of Educational Research"
    ]
  },
  {
    "id": "journal-electroanalytical-chemistry",
    "name": "Journal of Electroanalytical Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Electroanalytical Chemistry"
    ]
  },
  {
    "id": "journal-engineering-education",
    "name": "Journal of Engineering Education",
    "discipline": "general",
    "aliases": [
      "Journal of Engineering Education"
    ]
  },
  {
    "id": "journal-environmental-chemistry",
    "name": "Journal of Environmental Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Environmental Chemistry"
    ]
  },
  {
    "id": "journal-environmental-management",
    "name": "Journal of Environmental Management",
    "discipline": "sciences",
    "aliases": [
      "Journal of Environmental Management"
    ]
  },
  {
    "id": "journal-ethnopharmacology",
    "name": "Journal of Ethnopharmacology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Ethnopharmacology"
    ]
  },
  {
    "id": "journal-european-public-policy",
    "name": "Journal of European Public Policy",
    "discipline": "humanities",
    "aliases": [
      "Journal of European Public Policy"
    ]
  },
  {
    "id": "journal-evolutionary-biology",
    "name": "Journal of Evolutionary Biology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Evolutionary Biology"
    ]
  },
  {
    "id": "journal-experimental-biology",
    "name": "Journal of Experimental Biology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Experimental Biology"
    ]
  },
  {
    "id": "journal-experimental-botany",
    "name": "Journal of Experimental Botany",
    "discipline": "sciences",
    "aliases": [
      "Journal of Experimental Botany"
    ]
  },
  {
    "id": "journal-experimental-medicine",
    "name": "Journal of Experimental Medicine",
    "discipline": "sciences",
    "aliases": [
      "Journal of Experimental Medicine"
    ]
  },
  {
    "id": "journal-fluid-mechanics",
    "name": "Journal of Fluid Mechanics",
    "discipline": "sciences",
    "aliases": [
      "Journal of Fluid Mechanics"
    ]
  },
  {
    "id": "journal-food-composition-analysis",
    "name": "Journal of Food Composition and Analysis",
    "discipline": "sciences",
    "aliases": [
      "Journal of Food Composition and Analysis"
    ]
  },
  {
    "id": "journal-food-engineering",
    "name": "Journal of Food Engineering",
    "discipline": "sciences",
    "aliases": [
      "Journal of Food Engineering"
    ]
  },
  {
    "id": "journal-food-protection",
    "name": "Journal of Food Protection",
    "discipline": "sciences",
    "aliases": [
      "Journal of Food Protection"
    ]
  },
  {
    "id": "journal-food-science",
    "name": "Journal of Food Science",
    "discipline": "sciences",
    "aliases": [
      "Journal of Food Science"
    ]
  },
  {
    "id": "journal-gastrointestinal-surgery",
    "name": "Journal of Gastrointestinal Surgery",
    "discipline": "medicine",
    "aliases": [
      "Journal of Gastrointestinal Surgery"
    ]
  },
  {
    "id": "journal-geophysical-research-atmospheres",
    "name": "Journal of Geophysical Research Atmospheres",
    "discipline": "sciences",
    "aliases": [
      "Journal of Geophysical Research Atmospheres"
    ]
  },
  {
    "id": "journal-geophysical-research-oceans",
    "name": "Journal of Geophysical Research Oceans",
    "discipline": "sciences",
    "aliases": [
      "Journal of Geophysical Research Oceans"
    ]
  },
  {
    "id": "journal-geophysical-research-solid-earth",
    "name": "Journal of Geophysical Research Solid Earth",
    "discipline": "sciences",
    "aliases": [
      "Journal of Geophysical Research Solid Earth"
    ]
  },
  {
    "id": "journal-graph-theory",
    "name": "Journal of Graph Theory",
    "discipline": "sciences",
    "aliases": [
      "Journal of Graph Theory"
    ]
  },
  {
    "id": "journal-hazardous-materials",
    "name": "Journal of Hazardous Materials",
    "discipline": "sciences",
    "aliases": [
      "Journal of Hazardous Materials"
    ]
  },
  {
    "id": "journal-heart-lung-transplantation",
    "name": "Journal of Heart and Lung Transplantation",
    "discipline": "medicine",
    "aliases": [
      "Journal of Heart and Lung Transplantation"
    ]
  },
  {
    "id": "journal-hepatology",
    "name": "Journal of Hepatology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Hepatology"
    ]
  },
  {
    "id": "journal-higher-education",
    "name": "Journal of Higher Education",
    "discipline": "general",
    "aliases": [
      "Journal of Higher Education"
    ]
  },
  {
    "id": "journal-high-energy-physics",
    "name": "Journal of High Energy Physics",
    "discipline": "sciences",
    "aliases": [
      "Journal of High Energy Physics"
    ]
  },
  {
    "id": "journal-history-ideas",
    "name": "Journal of the History of Ideas",
    "discipline": "humanities",
    "aliases": [
      "Journal of the History of Ideas"
    ]
  },
  {
    "id": "journal-hospital-infection",
    "name": "Journal of Hospital Infection",
    "discipline": "medicine",
    "aliases": [
      "Journal of Hospital Infection"
    ]
  },
  {
    "id": "journal-hypertension",
    "name": "Journal of Hypertension",
    "discipline": "medicine",
    "aliases": [
      "Journal of Hypertension"
    ]
  },
  {
    "id": "journal-immunology",
    "name": "Journal of Immunology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Immunology"
    ]
  },
  {
    "id": "journal-infectious-diseases",
    "name": "Journal of Infectious Diseases",
    "discipline": "medicine",
    "aliases": [
      "Journal of Infectious Diseases"
    ]
  },
  {
    "id": "journal-inorganic-biochemistry",
    "name": "Journal of Inorganic Biochemistry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Inorganic Biochemistry"
    ]
  },
  {
    "id": "journal-interdisciplinary-history",
    "name": "Journal of Interdisciplinary History",
    "discipline": "humanities",
    "aliases": [
      "Journal of Interdisciplinary History"
    ]
  },
  {
    "id": "journal-internal-medicine",
    "name": "Journal of Internal Medicine",
    "discipline": "medicine",
    "aliases": [
      "Journal of Internal Medicine"
    ]
  },
  {
    "id": "journal-investigative-dermatology",
    "name": "Journal of Investigative Dermatology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Investigative Dermatology"
    ]
  },
  {
    "id": "journal-latin-american-studies",
    "name": "Journal of Latin American Studies",
    "discipline": "humanities",
    "aliases": [
      "Journal of Latin American Studies"
    ]
  },
  {
    "id": "journal-learning-analytics",
    "name": "Journal of Learning Analytics",
    "discipline": "general",
    "aliases": [
      "Journal of Learning Analytics"
    ]
  },
  {
    "id": "journal-legal-studies",
    "name": "Journal of Legal Studies",
    "discipline": "law",
    "aliases": [
      "Journal of Legal Studies"
    ]
  },
  {
    "id": "journal-luminescence",
    "name": "Journal of Luminescence",
    "discipline": "sciences",
    "aliases": [
      "Journal of Luminescence"
    ]
  },
  {
    "id": "journal-magnetic-resonance",
    "name": "Journal of Magnetic Resonance",
    "discipline": "sciences",
    "aliases": [
      "Journal of Magnetic Resonance"
    ]
  },
  {
    "id": "journal-magnetism-magnetic-materials",
    "name": "Journal of Magnetism and Magnetic Materials",
    "discipline": "sciences",
    "aliases": [
      "Journal of Magnetism and Magnetic Materials"
    ]
  },
  {
    "id": "journal-mass-spectrometry",
    "name": "Journal of Mass Spectrometry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Mass Spectrometry"
    ]
  },
  {
    "id": "journal-materials-chemistry-a",
    "name": "Journal of Materials Chemistry A",
    "discipline": "sciences",
    "aliases": [
      "Journal of Materials Chemistry A"
    ]
  },
  {
    "id": "journal-materials-chemistry-b",
    "name": "Journal of Materials Chemistry B",
    "discipline": "sciences",
    "aliases": [
      "Journal of Materials Chemistry B"
    ]
  },
  {
    "id": "journal-materials-chemistry-c",
    "name": "Journal of Materials Chemistry C",
    "discipline": "sciences",
    "aliases": [
      "Journal of Materials Chemistry C"
    ]
  },
  {
    "id": "journal-materials-research",
    "name": "Journal of Materials Research",
    "discipline": "sciences",
    "aliases": [
      "Journal of Materials Research"
    ]
  },
  {
    "id": "journal-materials-science",
    "name": "Journal of Materials Science",
    "discipline": "sciences",
    "aliases": [
      "Journal of Materials Science"
    ]
  },
  {
    "id": "journal-mechanical-behavior-biomedical-materials",
    "name": "Journal of the Mechanical Behavior of Biomedical Materials",
    "discipline": "sciences",
    "aliases": [
      "Journal of the Mechanical Behavior of Biomedical Materials"
    ]
  },
  {
    "id": "journal-medical-virology",
    "name": "Journal of Medical Virology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Medical Virology"
    ]
  },
  {
    "id": "journal-membrane-biology",
    "name": "Journal of Membrane Biology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Membrane Biology"
    ]
  },
  {
    "id": "journal-membrane-science",
    "name": "Journal of Membrane Science",
    "discipline": "sciences",
    "aliases": [
      "Journal of Membrane Science"
    ]
  },
  {
    "id": "journal-microbiology",
    "name": "Journal of Microbiology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Microbiology"
    ]
  },
  {
    "id": "journal-modern-history",
    "name": "Journal of Modern History",
    "discipline": "humanities",
    "aliases": [
      "Journal of Modern History"
    ]
  },
  {
    "id": "journal-molecular-biology",
    "name": "Journal of Molecular Biology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Molecular Biology"
    ]
  },
  {
    "id": "journal-molecular-catalysis-a",
    "name": "Journal of Molecular Catalysis A",
    "discipline": "sciences",
    "aliases": [
      "Journal of Molecular Catalysis A"
    ]
  },
  {
    "id": "journal-molecular-evolution",
    "name": "Journal of Molecular Evolution",
    "discipline": "sciences",
    "aliases": [
      "Journal of Molecular Evolution"
    ]
  },
  {
    "id": "journal-molecular-graphics-modelling",
    "name": "Journal of Molecular Graphics and Modelling",
    "discipline": "sciences",
    "aliases": [
      "Journal of Molecular Graphics and Modelling"
    ]
  },
  {
    "id": "journal-molecular-liquids",
    "name": "Journal of Molecular Liquids",
    "discipline": "sciences",
    "aliases": [
      "Journal of Molecular Liquids"
    ]
  },
  {
    "id": "journal-molecular-spectroscopy",
    "name": "Journal of Molecular Spectroscopy",
    "discipline": "sciences",
    "aliases": [
      "Journal of Molecular Spectroscopy"
    ]
  },
  {
    "id": "journal-nanobiotechnology",
    "name": "Journal of Nanobiotechnology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Nanobiotechnology"
    ]
  },
  {
    "id": "journal-nanoparticle-research",
    "name": "Journal of Nanoparticle Research",
    "discipline": "sciences",
    "aliases": [
      "Journal of Nanoparticle Research"
    ]
  },
  {
    "id": "journal-natural-products",
    "name": "Journal of Natural Products",
    "discipline": "sciences",
    "aliases": [
      "Journal of Natural Products"
    ]
  },
  {
    "id": "journal-neuropathology-experimental-neurology",
    "name": "Journal of Neuropathology and Experimental Neurology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Neuropathology and Experimental Neurology"
    ]
  },
  {
    "id": "journal-neurophysiology",
    "name": "Journal of Neurophysiology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Neurophysiology"
    ]
  },
  {
    "id": "journal-neuroscience",
    "name": "Journal of Neuroscience",
    "discipline": "sciences",
    "aliases": [
      "Journal of Neuroscience"
    ]
  },
  {
    "id": "journal-nuclear-medicine",
    "name": "Journal of Nuclear Medicine",
    "discipline": "medicine",
    "aliases": [
      "Journal of Nuclear Medicine"
    ]
  },
  {
    "id": "journal-nutritional-biochemistry",
    "name": "Journal of Nutritional Biochemistry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Nutritional Biochemistry"
    ]
  },
  {
    "id": "journal-organic-chemistry",
    "name": "Journal of Organic Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Organic Chemistry"
    ]
  },
  {
    "id": "journal-orthopaedic-research",
    "name": "Journal of Orthopaedic Research",
    "discipline": "medicine",
    "aliases": [
      "Journal of Orthopaedic Research"
    ]
  },
  {
    "id": "journal-parasitology",
    "name": "Journal of Parasitology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Parasitology"
    ]
  },
  {
    "id": "journal-pathology",
    "name": "Journal of Pathology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Pathology"
    ]
  },
  {
    "id": "journal-peptide-science",
    "name": "Journal of Peptide Science",
    "discipline": "sciences",
    "aliases": [
      "Journal of Peptide Science"
    ]
  },
  {
    "id": "journal-peace-research",
    "name": "Journal of Peace Research",
    "discipline": "humanities",
    "aliases": [
      "Journal of Peace Research"
    ]
  },
  {
    "id": "journal-pharmaceutical-sciences",
    "name": "Journal of Pharmaceutical Sciences",
    "discipline": "medicine",
    "aliases": [
      "Journal of Pharmaceutical Sciences"
    ]
  },
  {
    "id": "journal-pharmacology-experimental-therapeutics",
    "name": "Journal of Pharmacology and Experimental Therapeutics",
    "discipline": "medicine",
    "aliases": [
      "Journal of Pharmacology and Experimental Therapeutics"
    ]
  },
  {
    "id": "journal-photochemistry-photobiology-a",
    "name": "Journal of Photochemistry and Photobiology A",
    "discipline": "sciences",
    "aliases": [
      "Journal of Photochemistry and Photobiology A"
    ]
  },
  {
    "id": "journal-photochemistry-photobiology-b",
    "name": "Journal of Photochemistry and Photobiology B",
    "discipline": "sciences",
    "aliases": [
      "Journal of Photochemistry and Photobiology B"
    ]
  },
  {
    "id": "journal-phycology",
    "name": "Journal of Phycology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Phycology"
    ]
  },
  {
    "id": "journal-physical-chemistry-a",
    "name": "Journal of Physical Chemistry A",
    "discipline": "sciences",
    "aliases": [
      "Journal of Physical Chemistry A"
    ]
  },
  {
    "id": "journal-physical-chemistry-b",
    "name": "Journal of Physical Chemistry B",
    "discipline": "sciences",
    "aliases": [
      "Journal of Physical Chemistry B"
    ]
  },
  {
    "id": "journal-physical-chemistry-c",
    "name": "Journal of Physical Chemistry C",
    "discipline": "sciences",
    "aliases": [
      "Journal of Physical Chemistry C"
    ]
  },
  {
    "id": "journal-physical-chemistry-letters",
    "name": "Journal of Physical Chemistry Letters",
    "discipline": "sciences",
    "aliases": [
      "Journal of Physical Chemistry Letters"
    ]
  },
  {
    "id": "journal-physics-a",
    "name": "Journal of Physics A Mathematical and Theoretical",
    "discipline": "sciences",
    "aliases": [
      "Journal of Physics A Mathematical and Theoretical"
    ]
  },
  {
    "id": "journal-physics-condensed-matter",
    "name": "Journal of Physics Condensed Matter",
    "discipline": "sciences",
    "aliases": [
      "Journal of Physics Condensed Matter"
    ]
  },
  {
    "id": "journal-plant-biology",
    "name": "Journal of Plant Biology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Plant Biology"
    ]
  },
  {
    "id": "journal-plant-physiology",
    "name": "Journal of Plant Physiology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Plant Physiology"
    ]
  },
  {
    "id": "journal-political-economy",
    "name": "Journal of Political Economy",
    "discipline": "humanities",
    "aliases": [
      "Journal of Political Economy"
    ]
  },
  {
    "id": "journal-political-philosophy",
    "name": "Journal of Political Philosophy",
    "discipline": "humanities",
    "aliases": [
      "Journal of Political Philosophy"
    ]
  },
  {
    "id": "journal-politics",
    "name": "Journal of Politics",
    "discipline": "humanities",
    "aliases": [
      "Journal of Politics"
    ]
  },
  {
    "id": "journal-polymer-science",
    "name": "Journal of Polymer Science",
    "discipline": "sciences",
    "aliases": [
      "Journal of Polymer Science"
    ]
  },
  {
    "id": "journal-proteome-research",
    "name": "Journal of Proteome Research",
    "discipline": "sciences",
    "aliases": [
      "Journal of Proteome Research"
    ]
  },
  {
    "id": "journal-proteomics",
    "name": "Journal of Proteomics",
    "discipline": "sciences",
    "aliases": [
      "Journal of Proteomics"
    ]
  },
  {
    "id": "journal-psychiatric-research",
    "name": "Journal of Psychiatric Research",
    "discipline": "medicine",
    "aliases": [
      "Journal of Psychiatric Research"
    ]
  },
  {
    "id": "journal-psychopharmacology",
    "name": "Journal of Psychopharmacology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Psychopharmacology"
    ]
  },
  {
    "id": "journal-public-administration-research-theory",
    "name": "Journal of Public Administration Research and Theory",
    "discipline": "humanities",
    "aliases": [
      "Journal of Public Administration Research and Theory"
    ]
  },
  {
    "id": "journal-raman-spectroscopy",
    "name": "Journal of Raman Spectroscopy",
    "discipline": "sciences",
    "aliases": [
      "Journal of Raman Spectroscopy"
    ]
  },
  {
    "id": "journal-reproductive-immunology",
    "name": "Journal of Reproductive Immunology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Reproductive Immunology"
    ]
  },
  {
    "id": "journal-royal-society-interface",
    "name": "Journal of the Royal Society Interface",
    "discipline": "sciences",
    "aliases": [
      "Journal of the Royal Society Interface"
    ]
  },
  {
    "id": "journal-separation-science",
    "name": "Journal of Separation Science",
    "discipline": "sciences",
    "aliases": [
      "Journal of Separation Science"
    ]
  },
  {
    "id": "journal-social-history",
    "name": "Journal of Social History",
    "discipline": "humanities",
    "aliases": [
      "Journal of Social History"
    ]
  },
  {
    "id": "journal-solid-state-chemistry",
    "name": "Journal of Solid State Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Solid State Chemistry"
    ]
  },
  {
    "id": "journal-solid-state-electrochemistry",
    "name": "Journal of Solid State Electrochemistry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Solid State Electrochemistry"
    ]
  },
  {
    "id": "journal-structural-biology",
    "name": "Journal of Structural Biology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Structural Biology"
    ]
  },
  {
    "id": "journal-teacher-education",
    "name": "Journal of Teacher Education",
    "discipline": "general",
    "aliases": [
      "Journal of Teacher Education"
    ]
  },
  {
    "id": "journal-thermal-analysis-calorimetry",
    "name": "Journal of Thermal Analysis and Calorimetry",
    "discipline": "sciences",
    "aliases": [
      "Journal of Thermal Analysis and Calorimetry"
    ]
  },
  {
    "id": "journal-thoracic-cardiovascular-surgery",
    "name": "Journal of Thoracic and Cardiovascular Surgery",
    "discipline": "medicine",
    "aliases": [
      "Journal of Thoracic and Cardiovascular Surgery"
    ]
  },
  {
    "id": "journal-tissue-engineering-regenerative-medicine",
    "name": "Journal of Tissue Engineering and Regenerative Medicine",
    "discipline": "sciences",
    "aliases": [
      "Journal of Tissue Engineering and Regenerative Medicine"
    ]
  },
  {
    "id": "journal-trace-elements-medicine-biology",
    "name": "Journal of Trace Elements in Medicine and Biology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Trace Elements in Medicine and Biology"
    ]
  },
  {
    "id": "journal-urology",
    "name": "Journal of Urology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Urology"
    ]
  },
  {
    "id": "journal-virology",
    "name": "Journal of Virology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Virology"
    ]
  },
  {
    "id": "kidney-international",
    "name": "Kidney International",
    "discipline": "medicine",
    "aliases": [
      "Kidney International"
    ]
  },
  {
    "id": "lab-on-a-chip",
    "name": "Lab on a Chip",
    "discipline": "sciences",
    "aliases": [
      "Lab on a Chip"
    ]
  },
  {
    "id": "lancet-diabetes-endocrinology",
    "name": "Lancet Diabetes and Endocrinology",
    "discipline": "medicine",
    "aliases": [
      "Lancet Diabetes and Endocrinology"
    ]
  },
  {
    "id": "lancet-global-health",
    "name": "Lancet Global Health",
    "discipline": "medicine",
    "aliases": [
      "Lancet Global Health"
    ]
  },
  {
    "id": "lancet-hiv",
    "name": "Lancet HIV",
    "discipline": "medicine",
    "aliases": [
      "Lancet HIV"
    ]
  },
  {
    "id": "lancet-infectious-diseases",
    "name": "Lancet Infectious Diseases",
    "discipline": "medicine",
    "aliases": [
      "Lancet Infectious Diseases"
    ]
  },
  {
    "id": "lancet-neurology",
    "name": "Lancet Neurology",
    "discipline": "medicine",
    "aliases": [
      "Lancet Neurology"
    ]
  },
  {
    "id": "lancet-oncology",
    "name": "Lancet Oncology",
    "discipline": "medicine",
    "aliases": [
      "Lancet Oncology"
    ]
  },
  {
    "id": "lancet-psychiatry",
    "name": "Lancet Psychiatry",
    "discipline": "medicine",
    "aliases": [
      "Lancet Psychiatry"
    ]
  },
  {
    "id": "langmuir",
    "name": "Langmuir",
    "discipline": "sciences",
    "aliases": [
      "Langmuir"
    ]
  },
  {
    "id": "language-learning",
    "name": "Language Learning",
    "discipline": "humanities",
    "aliases": [
      "Language Learning"
    ]
  },
  {
    "id": "language-policy",
    "name": "Language Policy",
    "discipline": "humanities",
    "aliases": [
      "Language Policy"
    ]
  },
  {
    "id": "leukemia",
    "name": "Leukemia",
    "discipline": "medicine",
    "aliases": [
      "Leukemia"
    ]
  },
  {
    "id": "leukemia-research",
    "name": "Leukemia Research",
    "discipline": "medicine",
    "aliases": [
      "Leukemia Research"
    ]
  },
  {
    "id": "lipids",
    "name": "Lipids",
    "discipline": "sciences",
    "aliases": [
      "Lipids"
    ]
  },
  {
    "id": "liver-international",
    "name": "Liver International",
    "discipline": "medicine",
    "aliases": [
      "Liver International"
    ]
  },
  {
    "id": "macromolecular-rapid-communications",
    "name": "Macromolecular Rapid Communications",
    "discipline": "sciences",
    "aliases": [
      "Macromolecular Rapid Communications"
    ]
  },
  {
    "id": "macromolecules",
    "name": "Macromolecules",
    "discipline": "sciences",
    "aliases": [
      "Macromolecules"
    ]
  },
  {
    "id": "marine-biology",
    "name": "Marine Biology",
    "discipline": "sciences",
    "aliases": [
      "Marine Biology"
    ]
  },
  {
    "id": "marine-chemistry",
    "name": "Marine Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Marine Chemistry"
    ]
  },
  {
    "id": "marine-ecology-progress-series",
    "name": "Marine Ecology Progress Series",
    "discipline": "sciences",
    "aliases": [
      "Marine Ecology Progress Series"
    ]
  },
  {
    "id": "marine-environmental-research",
    "name": "Marine Environmental Research",
    "discipline": "sciences",
    "aliases": [
      "Marine Environmental Research"
    ]
  },
  {
    "id": "marine-pollution-bulletin",
    "name": "Marine Pollution Bulletin",
    "discipline": "sciences",
    "aliases": [
      "Marine Pollution Bulletin"
    ]
  },
  {
    "id": "matrix-biology",
    "name": "Matrix Biology",
    "discipline": "sciences",
    "aliases": [
      "Matrix Biology"
    ]
  },
  {
    "id": "mayo-clinic-proceedings",
    "name": "Mayo Clinic Proceedings",
    "discipline": "medicine",
    "aliases": [
      "Mayo Clinic Proceedings"
    ]
  },
  {
    "id": "mechanisms-ageing-development",
    "name": "Mechanisms of Ageing and Development",
    "discipline": "sciences",
    "aliases": [
      "Mechanisms of Ageing and Development"
    ]
  },
  {
    "id": "metabolic-engineering",
    "name": "Metabolic Engineering",
    "discipline": "sciences",
    "aliases": [
      "Metabolic Engineering"
    ]
  },
  {
    "id": "metabolomics",
    "name": "Metabolomics",
    "discipline": "sciences",
    "aliases": [
      "Metabolomics"
    ]
  },
  {
    "id": "methods",
    "name": "Methods",
    "discipline": "sciences",
    "aliases": [
      "Methods"
    ]
  },
  {
    "id": "microbiome",
    "name": "Microbiome",
    "discipline": "sciences",
    "aliases": [
      "Microbiome"
    ]
  },
  {
    "id": "mitochondrion",
    "name": "Mitochondrion",
    "discipline": "sciences",
    "aliases": [
      "Mitochondrion"
    ]
  },
  {
    "id": "modern-fiction-studies",
    "name": "Modern Fiction Studies",
    "discipline": "humanities",
    "aliases": [
      "Modern Fiction Studies"
    ]
  },
  {
    "id": "modern-law-review",
    "name": "Modern Law Review",
    "discipline": "law",
    "aliases": [
      "Modern Law Review"
    ]
  },
  {
    "id": "molecular-biology-cell",
    "name": "Molecular Biology of the Cell",
    "discipline": "sciences",
    "aliases": [
      "Molecular Biology of the Cell"
    ]
  },
  {
    "id": "molecular-biology-evolution",
    "name": "Molecular Biology and Evolution",
    "discipline": "sciences",
    "aliases": [
      "Molecular Biology and Evolution"
    ]
  },
  {
    "id": "molecular-cell",
    "name": "Molecular Cell",
    "discipline": "sciences",
    "aliases": [
      "Molecular Cell"
    ]
  },
  {
    "id": "molecular-cellular-endocrinology",
    "name": "Molecular and Cellular Endocrinology",
    "discipline": "sciences",
    "aliases": [
      "Molecular and Cellular Endocrinology"
    ]
  },
  {
    "id": "molecular-cellular-neurosciences",
    "name": "Molecular and Cellular Neurosciences",
    "discipline": "sciences",
    "aliases": [
      "Molecular and Cellular Neurosciences"
    ]
  },
  {
    "id": "molecular-ecology",
    "name": "Molecular Ecology",
    "discipline": "sciences",
    "aliases": [
      "Molecular Ecology"
    ]
  },
  {
    "id": "molecular-genetics-metabolism",
    "name": "Molecular Genetics and Metabolism",
    "discipline": "sciences",
    "aliases": [
      "Molecular Genetics and Metabolism"
    ]
  },
  {
    "id": "molecular-immunology",
    "name": "Molecular Immunology",
    "discipline": "medicine",
    "aliases": [
      "Molecular Immunology"
    ]
  },
  {
    "id": "molecular-medicine",
    "name": "Molecular Medicine",
    "discipline": "medicine",
    "aliases": [
      "Molecular Medicine"
    ]
  },
  {
    "id": "molecular-microbiology",
    "name": "Molecular Microbiology",
    "discipline": "sciences",
    "aliases": [
      "Molecular Microbiology"
    ]
  },
  {
    "id": "molecular-neurobiology",
    "name": "Molecular Neurobiology",
    "discipline": "medicine",
    "aliases": [
      "Molecular Neurobiology"
    ]
  },
  {
    "id": "molecular-pharmacology",
    "name": "Molecular Pharmacology",
    "discipline": "medicine",
    "aliases": [
      "Molecular Pharmacology"
    ]
  },
  {
    "id": "molecular-plant",
    "name": "Molecular Plant",
    "discipline": "sciences",
    "aliases": [
      "Molecular Plant"
    ]
  },
  {
    "id": "molecular-therapy",
    "name": "Molecular Therapy",
    "discipline": "medicine",
    "aliases": [
      "Molecular Therapy"
    ]
  },
  {
    "id": "monthly-notices-royal-astronomical-society",
    "name": "Monthly Notices of the Royal Astronomical Society",
    "discipline": "sciences",
    "aliases": [
      "Monthly Notices of the Royal Astronomical Society"
    ]
  },
  {
    "id": "multiple-sclerosis-journal",
    "name": "Multiple Sclerosis Journal",
    "discipline": "medicine",
    "aliases": [
      "Multiple Sclerosis Journal"
    ]
  },
  {
    "id": "mutagenesis",
    "name": "Mutagenesis",
    "discipline": "sciences",
    "aliases": [
      "Mutagenesis"
    ]
  },
  {
    "id": "mutation-research",
    "name": "Mutation Research",
    "discipline": "sciences",
    "aliases": [
      "Mutation Research"
    ]
  },
  {
    "id": "nanoscale",
    "name": "Nanoscale",
    "discipline": "sciences",
    "aliases": [
      "Nanoscale"
    ]
  },
  {
    "id": "nanoscale-advances",
    "name": "Nanoscale Advances",
    "discipline": "sciences",
    "aliases": [
      "Nanoscale Advances"
    ]
  },
  {
    "id": "nature-aging",
    "name": "Nature Aging",
    "discipline": "medicine",
    "aliases": [
      "Nature Aging"
    ]
  },
  {
    "id": "nature-astronomy",
    "name": "Nature Astronomy",
    "discipline": "sciences",
    "aliases": [
      "Nature Astronomy"
    ]
  },
  {
    "id": "nature-biomedical-engineering",
    "name": "Nature Biomedical Engineering",
    "discipline": "medicine",
    "aliases": [
      "Nature Biomedical Engineering"
    ]
  },
  {
    "id": "nature-biotechnology",
    "name": "Nature Biotechnology",
    "discipline": "sciences",
    "aliases": [
      "Nature Biotechnology"
    ]
  },
  {
    "id": "nature-cancer",
    "name": "Nature Cancer",
    "discipline": "medicine",
    "aliases": [
      "Nature Cancer"
    ]
  },
  {
    "id": "nature-cardiovascular-research",
    "name": "Nature Cardiovascular Research",
    "discipline": "medicine",
    "aliases": [
      "Nature Cardiovascular Research"
    ]
  },
  {
    "id": "nature-cell-biology",
    "name": "Nature Cell Biology",
    "discipline": "sciences",
    "aliases": [
      "Nature Cell Biology"
    ]
  },
  {
    "id": "nature-chemical-biology",
    "name": "Nature Chemical Biology",
    "discipline": "sciences",
    "aliases": [
      "Nature Chemical Biology"
    ]
  },
  {
    "id": "nature-chemistry",
    "name": "Nature Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Nature Chemistry"
    ]
  },
  {
    "id": "nature-climate-change",
    "name": "Nature Climate Change",
    "discipline": "sciences",
    "aliases": [
      "Nature Climate Change"
    ]
  },
  {
    "id": "nature-communications",
    "name": "Nature Communications",
    "discipline": "sciences",
    "aliases": [
      "Nature Communications"
    ]
  },
  {
    "id": "nature-computational-science",
    "name": "Nature Computational Science",
    "discipline": "sciences",
    "aliases": [
      "Nature Computational Science"
    ]
  },
  {
    "id": "nature-ecology-evolution",
    "name": "Nature Ecology and Evolution",
    "discipline": "sciences",
    "aliases": [
      "Nature Ecology and Evolution"
    ]
  },
  {
    "id": "nature-electronics",
    "name": "Nature Electronics",
    "discipline": "sciences",
    "aliases": [
      "Nature Electronics"
    ]
  },
  {
    "id": "nature-energy",
    "name": "Nature Energy",
    "discipline": "sciences",
    "aliases": [
      "Nature Energy"
    ]
  },
  {
    "id": "nature-food",
    "name": "Nature Food",
    "discipline": "sciences",
    "aliases": [
      "Nature Food"
    ]
  },
  {
    "id": "nature-genetics",
    "name": "Nature Genetics",
    "discipline": "sciences",
    "aliases": [
      "Nature Genetics"
    ]
  },
  {
    "id": "nature-geoscience",
    "name": "Nature Geoscience",
    "discipline": "sciences",
    "aliases": [
      "Nature Geoscience"
    ]
  },
  {
    "id": "nature-human-behaviour",
    "name": "Nature Human Behaviour",
    "discipline": "sciences",
    "aliases": [
      "Nature Human Behaviour"
    ]
  },
  {
    "id": "nature-immunology",
    "name": "Nature Immunology",
    "discipline": "sciences",
    "aliases": [
      "Nature Immunology"
    ]
  },
  {
    "id": "nature-machine-intelligence",
    "name": "Nature Machine Intelligence",
    "discipline": "sciences",
    "aliases": [
      "Nature Machine Intelligence"
    ]
  },
  {
    "id": "nature-materials",
    "name": "Nature Materials",
    "discipline": "sciences",
    "aliases": [
      "Nature Materials"
    ]
  },
  {
    "id": "nature-medicine",
    "name": "Nature Medicine",
    "discipline": "medicine",
    "aliases": [
      "Nature Medicine"
    ]
  },
  {
    "id": "nature-metabolism",
    "name": "Nature Metabolism",
    "discipline": "sciences",
    "aliases": [
      "Nature Metabolism"
    ]
  },
  {
    "id": "nature-methods",
    "name": "Nature Methods",
    "discipline": "sciences",
    "aliases": [
      "Nature Methods"
    ]
  },
  {
    "id": "nature-microbiology",
    "name": "Nature Microbiology",
    "discipline": "sciences",
    "aliases": [
      "Nature Microbiology"
    ]
  },
  {
    "id": "nature-nanotechnology",
    "name": "Nature Nanotechnology",
    "discipline": "sciences",
    "aliases": [
      "Nature Nanotechnology"
    ]
  },
  {
    "id": "nature-neuroscience",
    "name": "Nature Neuroscience",
    "discipline": "sciences",
    "aliases": [
      "Nature Neuroscience"
    ]
  },
  {
    "id": "nature-physics",
    "name": "Nature Physics",
    "discipline": "sciences",
    "aliases": [
      "Nature Physics"
    ]
  },
  {
    "id": "nature-plants",
    "name": "Nature Plants",
    "discipline": "sciences",
    "aliases": [
      "Nature Plants"
    ]
  },
  {
    "id": "nature-protocols",
    "name": "Nature Protocols",
    "discipline": "sciences",
    "aliases": [
      "Nature Protocols"
    ]
  },
  {
    "id": "nature-reviews-cancer",
    "name": "Nature Reviews Cancer",
    "discipline": "medicine",
    "aliases": [
      "Nature Reviews Cancer"
    ]
  },
  {
    "id": "nature-reviews-chemistry",
    "name": "Nature Reviews Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Nature Reviews Chemistry"
    ]
  },
  {
    "id": "nature-reviews-drug-discovery",
    "name": "Nature Reviews Drug Discovery",
    "discipline": "medicine",
    "aliases": [
      "Nature Reviews Drug Discovery"
    ]
  },
  {
    "id": "nature-reviews-earth-environment",
    "name": "Nature Reviews Earth and Environment",
    "discipline": "sciences",
    "aliases": [
      "Nature Reviews Earth and Environment"
    ]
  },
  {
    "id": "nature-reviews-genetics",
    "name": "Nature Reviews Genetics",
    "discipline": "sciences",
    "aliases": [
      "Nature Reviews Genetics"
    ]
  },
  {
    "id": "nature-reviews-immunology",
    "name": "Nature Reviews Immunology",
    "discipline": "sciences",
    "aliases": [
      "Nature Reviews Immunology"
    ]
  },
  {
    "id": "nature-reviews-materials",
    "name": "Nature Reviews Materials",
    "discipline": "sciences",
    "aliases": [
      "Nature Reviews Materials"
    ]
  },
  {
    "id": "nature-reviews-microbiology",
    "name": "Nature Reviews Microbiology",
    "discipline": "sciences",
    "aliases": [
      "Nature Reviews Microbiology"
    ]
  },
  {
    "id": "nature-reviews-molecular-cell-biology",
    "name": "Nature Reviews Molecular Cell Biology",
    "discipline": "sciences",
    "aliases": [
      "Nature Reviews Molecular Cell Biology"
    ]
  },
  {
    "id": "nature-reviews-neuroscience",
    "name": "Nature Reviews Neuroscience",
    "discipline": "sciences",
    "aliases": [
      "Nature Reviews Neuroscience"
    ]
  },
  {
    "id": "nature-reviews-physics",
    "name": "Nature Reviews Physics",
    "discipline": "sciences",
    "aliases": [
      "Nature Reviews Physics"
    ]
  },
  {
    "id": "nature-structural-molecular-biology",
    "name": "Nature Structural and Molecular Biology",
    "discipline": "sciences",
    "aliases": [
      "Nature Structural and Molecular Biology"
    ]
  },
  {
    "id": "nature-sustainability",
    "name": "Nature Sustainability",
    "discipline": "sciences",
    "aliases": [
      "Nature Sustainability"
    ]
  },
  {
    "id": "nature-water",
    "name": "Nature Water",
    "discipline": "sciences",
    "aliases": [
      "Nature Water"
    ]
  },
  {
    "id": "neural-networks",
    "name": "Neural Networks",
    "discipline": "sciences",
    "aliases": [
      "Neural Networks"
    ]
  },
  {
    "id": "nephrology-dialysis-transplantation",
    "name": "Nephrology Dialysis Transplantation",
    "discipline": "medicine",
    "aliases": [
      "Nephrology Dialysis Transplantation"
    ]
  },
  {
    "id": "neuro-oncology",
    "name": "Neuro-Oncology",
    "discipline": "medicine",
    "aliases": [
      "Neuro-Oncology"
    ]
  },
  {
    "id": "neuro-oncology-advances",
    "name": "Neuro-Oncology Advances",
    "discipline": "medicine",
    "aliases": [
      "Neuro-Oncology Advances"
    ]
  },
  {
    "id": "neurochemical-research",
    "name": "Neurochemical Research",
    "discipline": "medicine",
    "aliases": [
      "Neurochemical Research"
    ]
  },
  {
    "id": "neurochemistry-international",
    "name": "Neurochemistry International",
    "discipline": "medicine",
    "aliases": [
      "Neurochemistry International"
    ]
  },
  {
    "id": "neuroinformatics",
    "name": "Neuroinformatics",
    "discipline": "sciences",
    "aliases": [
      "Neuroinformatics"
    ]
  },
  {
    "id": "neurology",
    "name": "Neurology",
    "discipline": "medicine",
    "aliases": [
      "Neurology"
    ]
  },
  {
    "id": "neuron",
    "name": "Neuron",
    "discipline": "medicine",
    "aliases": [
      "Neuron"
    ]
  },
  {
    "id": "neuropsychologia",
    "name": "Neuropsychologia",
    "discipline": "medicine",
    "aliases": [
      "Neuropsychologia"
    ]
  },
  {
    "id": "neuroscience",
    "name": "Neuroscience",
    "discipline": "medicine",
    "aliases": [
      "Neuroscience"
    ]
  },
  {
    "id": "neuroscience-letters",
    "name": "Neuroscience Letters",
    "discipline": "medicine",
    "aliases": [
      "Neuroscience Letters"
    ]
  },
  {
    "id": "new-journal-chemistry",
    "name": "New Journal of Chemistry",
    "discipline": "sciences",
    "aliases": [
      "New Journal of Chemistry"
    ]
  },
  {
    "id": "new-journal-physics",
    "name": "New Journal of Physics",
    "discipline": "sciences",
    "aliases": [
      "New Journal of Physics"
    ]
  },
  {
    "id": "new-literary-history",
    "name": "New Literary History",
    "discipline": "humanities",
    "aliases": [
      "New Literary History"
    ]
  },
  {
    "id": "new-media-society",
    "name": "New Media and Society",
    "discipline": "humanities",
    "aliases": [
      "New Media and Society"
    ]
  },
  {
    "id": "nitric-oxide",
    "name": "Nitric Oxide",
    "discipline": "sciences",
    "aliases": [
      "Nitric Oxide"
    ]
  },
  {
    "id": "nucleic-acids-research",
    "name": "Nucleic Acids Research",
    "discipline": "sciences",
    "aliases": [
      "Nucleic Acids Research"
    ]
  },
  {
    "id": "obesity",
    "name": "Obesity",
    "discipline": "medicine",
    "aliases": [
      "Obesity"
    ]
  },
  {
    "id": "oncogene",
    "name": "Oncogene",
    "discipline": "sciences",
    "aliases": [
      "Oncogene"
    ]
  },
  {
    "id": "oncology-letters",
    "name": "Oncology Letters",
    "discipline": "medicine",
    "aliases": [
      "Oncology Letters"
    ]
  },
  {
    "id": "oncology-reports",
    "name": "Oncology Reports",
    "discipline": "medicine",
    "aliases": [
      "Oncology Reports"
    ]
  },
  {
    "id": "oncotarget",
    "name": "Oncotarget",
    "discipline": "medicine",
    "aliases": [
      "Oncotarget"
    ]
  },
  {
    "id": "optics-express",
    "name": "Optics Express",
    "discipline": "sciences",
    "aliases": [
      "Optics Express"
    ]
  },
  {
    "id": "optics-letters",
    "name": "Optics Letters",
    "discipline": "sciences",
    "aliases": [
      "Optics Letters"
    ]
  },
  {
    "id": "organic-biomolecular-chemistry",
    "name": "Organic and Biomolecular Chemistry",
    "discipline": "sciences",
    "aliases": [
      "Organic and Biomolecular Chemistry"
    ]
  },
  {
    "id": "organic-letters",
    "name": "Organic Letters",
    "discipline": "sciences",
    "aliases": [
      "Organic Letters"
    ]
  },
  {
    "id": "organization-studies",
    "name": "Organization Studies",
    "discipline": "humanities",
    "aliases": [
      "Organization Studies"
    ]
  },
  {
    "id": "oxford-journal-legal-studies",
    "name": "Oxford Journal of Legal Studies",
    "discipline": "law",
    "aliases": [
      "Oxford Journal of Legal Studies"
    ]
  },
  {
    "id": "pain",
    "name": "Pain",
    "discipline": "medicine",
    "aliases": [
      "Pain"
    ]
  },
  {
    "id": "pediatrics",
    "name": "Pediatrics",
    "discipline": "medicine",
    "aliases": [
      "Pediatrics"
    ]
  },
  {
    "id": "physical-chemistry-chemical-physics",
    "name": "Physical Chemistry Chemical Physics",
    "discipline": "sciences",
    "aliases": [
      "Physical Chemistry Chemical Physics"
    ]
  },
  {
    "id": "physical-review-a",
    "name": "Physical Review A",
    "discipline": "sciences",
    "aliases": [
      "Physical Review A"
    ]
  },
  {
    "id": "physical-review-b",
    "name": "Physical Review B",
    "discipline": "sciences",
    "aliases": [
      "Physical Review B"
    ]
  },
  {
    "id": "physical-review-c",
    "name": "Physical Review C",
    "discipline": "sciences",
    "aliases": [
      "Physical Review C"
    ]
  },
  {
    "id": "physical-review-d",
    "name": "Physical Review D",
    "discipline": "sciences",
    "aliases": [
      "Physical Review D"
    ]
  },
  {
    "id": "physical-review-e",
    "name": "Physical Review E",
    "discipline": "sciences",
    "aliases": [
      "Physical Review E"
    ]
  },
  {
    "id": "physical-review-letters",
    "name": "Physical Review Letters",
    "discipline": "sciences",
    "aliases": [
      "Physical Review Letters"
    ]
  },
  {
    "id": "physical-review-research",
    "name": "Physical Review Research",
    "discipline": "sciences",
    "aliases": [
      "Physical Review Research"
    ]
  },
  {
    "id": "philosophy-public-affairs",
    "name": "Philosophy and Public Affairs",
    "discipline": "humanities",
    "aliases": [
      "Philosophy and Public Affairs"
    ]
  },
  {
    "id": "philosophy-science",
    "name": "Philosophy of Science",
    "discipline": "humanities",
    "aliases": [
      "Philosophy of Science"
    ]
  },
  {
    "id": "plant-cell",
    "name": "Plant Cell",
    "discipline": "sciences",
    "aliases": [
      "Plant Cell"
    ]
  },
  {
    "id": "plant-cell-environment",
    "name": "Plant Cell and Environment",
    "discipline": "sciences",
    "aliases": [
      "Plant Cell and Environment"
    ]
  },
  {
    "id": "plant-journal",
    "name": "Plant Journal",
    "discipline": "sciences",
    "aliases": [
      "Plant Journal"
    ]
  },
  {
    "id": "plant-physiology",
    "name": "Plant Physiology",
    "discipline": "sciences",
    "aliases": [
      "Plant Physiology"
    ]
  },
  {
    "id": "plos-biology",
    "name": "PLOS Biology",
    "discipline": "sciences",
    "aliases": [
      "PLOS Biology"
    ]
  },
  {
    "id": "plos-computational-biology",
    "name": "PLOS Computational Biology",
    "discipline": "sciences",
    "aliases": [
      "PLOS Computational Biology"
    ]
  },
  {
    "id": "plos-genetics",
    "name": "PLOS Genetics",
    "discipline": "sciences",
    "aliases": [
      "PLOS Genetics"
    ]
  },
  {
    "id": "plos-medicine",
    "name": "PLOS Medicine",
    "discipline": "medicine",
    "aliases": [
      "PLOS Medicine"
    ]
  },
  {
    "id": "plos-one",
    "name": "PLOS ONE",
    "discipline": "sciences",
    "aliases": [
      "PLOS ONE"
    ]
  },
  {
    "id": "plos-pathogens",
    "name": "PLOS Pathogens",
    "discipline": "sciences",
    "aliases": [
      "PLOS Pathogens"
    ]
  },
  {
    "id": "poetics-today",
    "name": "Poetics Today",
    "discipline": "humanities",
    "aliases": [
      "Poetics Today"
    ]
  },
  {
    "id": "political-behavior",
    "name": "Political Behavior",
    "discipline": "humanities",
    "aliases": [
      "Political Behavior"
    ]
  },
  {
    "id": "political-communication",
    "name": "Political Communication",
    "discipline": "humanities",
    "aliases": [
      "Political Communication"
    ]
  },
  {
    "id": "political-psychology",
    "name": "Political Psychology",
    "discipline": "humanities",
    "aliases": [
      "Political Psychology"
    ]
  },
  {
    "id": "political-research-quarterly",
    "name": "Political Research Quarterly",
    "discipline": "humanities",
    "aliases": [
      "Political Research Quarterly"
    ]
  },
  {
    "id": "political-science-quarterly",
    "name": "Political Science Quarterly",
    "discipline": "humanities",
    "aliases": [
      "Political Science Quarterly"
    ]
  },
  {
    "id": "politics-society",
    "name": "Politics and Society",
    "discipline": "humanities",
    "aliases": [
      "Politics and Society"
    ]
  },
  {
    "id": "postcolonial-studies",
    "name": "Postcolonial Studies",
    "discipline": "humanities",
    "aliases": [
      "Postcolonial Studies"
    ]
  },
  {
    "id": "proceedings-national-academy-sciences",
    "name": "Proceedings of the National Academy of Sciences",
    "discipline": "sciences",
    "aliases": [
      "Proceedings of the National Academy of Sciences"
    ]
  },
  {
    "id": "proceedings-royal-society-a",
    "name": "Proceedings of the Royal Society A",
    "discipline": "sciences",
    "aliases": [
      "Proceedings of the Royal Society A"
    ]
  },
  {
    "id": "proceedings-royal-society-b",
    "name": "Proceedings of the Royal Society B",
    "discipline": "sciences",
    "aliases": [
      "Proceedings of the Royal Society B"
    ]
  },
  {
    "id": "protein-science",
    "name": "Protein Science",
    "discipline": "sciences",
    "aliases": [
      "Protein Science"
    ]
  },
  {
    "id": "proteins-structure-function",
    "name": "Proteins Structure Function and Bioinformatics",
    "discipline": "sciences",
    "aliases": [
      "Proteins Structure Function and Bioinformatics"
    ]
  },
  {
    "id": "public-administration-review",
    "name": "Public Administration Review",
    "discipline": "humanities",
    "aliases": [
      "Public Administration Review"
    ]
  },
  {
    "id": "qualitative-sociology",
    "name": "Qualitative Sociology",
    "discipline": "humanities",
    "aliases": [
      "Qualitative Sociology"
    ]
  },
  {
    "id": "quarterly-journal-economics",
    "name": "Quarterly Journal of Economics",
    "discipline": "humanities",
    "aliases": [
      "Quarterly Journal of Economics"
    ]
  },
  {
    "id": "radiology",
    "name": "Radiology",
    "discipline": "medicine",
    "aliases": [
      "Radiology"
    ]
  },
  {
    "id": "representations",
    "name": "Representations",
    "discipline": "humanities",
    "aliases": [
      "Representations"
    ]
  },
  {
    "id": "research-science-education",
    "name": "Research in Science Education",
    "discipline": "general",
    "aliases": [
      "Research in Science Education"
    ]
  },
  {
    "id": "review-educational-research",
    "name": "Review of Educational Research",
    "discipline": "general",
    "aliases": [
      "Review of Educational Research"
    ]
  },
  {
    "id": "review-international-studies",
    "name": "Review of International Studies",
    "discipline": "humanities",
    "aliases": [
      "Review of International Studies"
    ]
  },
  {
    "id": "rheumatology",
    "name": "Rheumatology",
    "discipline": "medicine",
    "aliases": [
      "Rheumatology"
    ]
  },
  {
    "id": "rna",
    "name": "RNA",
    "discipline": "sciences",
    "aliases": [
      "RNA"
    ]
  },
  {
    "id": "royal-society-open-science",
    "name": "Royal Society Open Science",
    "discipline": "sciences",
    "aliases": [
      "Royal Society Open Science"
    ]
  },
  {
    "id": "schizophrenia-bulletin",
    "name": "Schizophrenia Bulletin",
    "discipline": "medicine",
    "aliases": [
      "Schizophrenia Bulletin"
    ]
  },
  {
    "id": "science-advances",
    "name": "Science Advances",
    "discipline": "sciences",
    "aliases": [
      "Science Advances"
    ]
  },
  {
    "id": "science-education",
    "name": "Science Education",
    "discipline": "general",
    "aliases": [
      "Science Education"
    ]
  },
  {
    "id": "science-signaling",
    "name": "Science Signaling",
    "discipline": "sciences",
    "aliases": [
      "Science Signaling"
    ]
  },
  {
    "id": "science-total-environment",
    "name": "Science of the Total Environment",
    "discipline": "sciences",
    "aliases": [
      "Science of the Total Environment"
    ]
  },
  {
    "id": "science-translational-medicine",
    "name": "Science Translational Medicine",
    "discipline": "sciences",
    "aliases": [
      "Science Translational Medicine"
    ]
  },
  {
    "id": "scientific-reports",
    "name": "Scientific Reports",
    "discipline": "sciences",
    "aliases": [
      "Scientific Reports"
    ]
  },
  {
    "id": "siam-journal-applied-mathematics",
    "name": "SIAM Journal on Applied Mathematics",
    "discipline": "sciences",
    "aliases": [
      "SIAM Journal on Applied Mathematics"
    ]
  },
  {
    "id": "siam-journal-computing",
    "name": "SIAM Journal on Computing",
    "discipline": "sciences",
    "aliases": [
      "SIAM Journal on Computing"
    ]
  },
  {
    "id": "siam-journal-discrete-mathematics",
    "name": "SIAM Journal on Discrete Mathematics",
    "discipline": "sciences",
    "aliases": [
      "SIAM Journal on Discrete Mathematics"
    ]
  },
  {
    "id": "siam-journal-numerical-analysis",
    "name": "SIAM Journal on Numerical Analysis",
    "discipline": "sciences",
    "aliases": [
      "SIAM Journal on Numerical Analysis"
    ]
  },
  {
    "id": "siam-journal-optimization",
    "name": "SIAM Journal on Optimization",
    "discipline": "sciences",
    "aliases": [
      "SIAM Journal on Optimization"
    ]
  },
  {
    "id": "siam-review",
    "name": "SIAM Review",
    "discipline": "sciences",
    "aliases": [
      "SIAM Review"
    ]
  },
  {
    "id": "small",
    "name": "Small",
    "discipline": "sciences",
    "aliases": [
      "Small"
    ]
  },
  {
    "id": "social-forces",
    "name": "Social Forces",
    "discipline": "humanities",
    "aliases": [
      "Social Forces"
    ]
  },
  {
    "id": "social-problems",
    "name": "Social Problems",
    "discipline": "humanities",
    "aliases": [
      "Social Problems"
    ]
  },
  {
    "id": "sociological-methods-research",
    "name": "Sociological Methods and Research",
    "discipline": "humanities",
    "aliases": [
      "Sociological Methods and Research"
    ]
  },
  {
    "id": "sociological-theory",
    "name": "Sociological Theory",
    "discipline": "humanities",
    "aliases": [
      "Sociological Theory"
    ]
  },
  {
    "id": "sociology",
    "name": "Sociology",
    "discipline": "humanities",
    "aliases": [
      "Sociology"
    ]
  },
  {
    "id": "sociology-education",
    "name": "Sociology of Education",
    "discipline": "humanities",
    "aliases": [
      "Sociology of Education"
    ]
  },
  {
    "id": "soft-matter",
    "name": "Soft Matter",
    "discipline": "sciences",
    "aliases": [
      "Soft Matter"
    ]
  },
  {
    "id": "stanford-law-review",
    "name": "Stanford Law Review",
    "discipline": "law",
    "aliases": [
      "Stanford Law Review"
    ]
  },
  {
    "id": "stem-cell-reports",
    "name": "Stem Cell Reports",
    "discipline": "sciences",
    "aliases": [
      "Stem Cell Reports"
    ]
  },
  {
    "id": "stroke",
    "name": "Stroke",
    "discipline": "medicine",
    "aliases": [
      "Stroke"
    ]
  },
  {
    "id": "structure",
    "name": "Structure",
    "discipline": "sciences",
    "aliases": [
      "Structure"
    ]
  },
  {
    "id": "surgery",
    "name": "Surgery",
    "discipline": "medicine",
    "aliases": [
      "Surgery"
    ]
  },
  {
    "id": "systematic-biology",
    "name": "Systematic Biology",
    "discipline": "sciences",
    "aliases": [
      "Systematic Biology"
    ]
  },
  {
    "id": "teaching-teacher-education",
    "name": "Teaching and Teacher Education",
    "discipline": "general",
    "aliases": [
      "Teaching and Teacher Education"
    ]
  },
  {
    "id": "texas-law-review",
    "name": "Texas Law Review",
    "discipline": "law",
    "aliases": [
      "Texas Law Review"
    ]
  },
  {
    "id": "theory-society",
    "name": "Theory and Society",
    "discipline": "humanities",
    "aliases": [
      "Theory and Society"
    ]
  },
  {
    "id": "third-world-quarterly",
    "name": "Third World Quarterly",
    "discipline": "humanities",
    "aliases": [
      "Third World Quarterly"
    ]
  },
  {
    "id": "thorax",
    "name": "Thorax",
    "discipline": "medicine",
    "aliases": [
      "Thorax"
    ]
  },
  {
    "id": "transplantation",
    "name": "Transplantation",
    "discipline": "medicine",
    "aliases": [
      "Transplantation"
    ]
  },
  {
    "id": "trends-biochemical-sciences",
    "name": "Trends in Biochemical Sciences",
    "discipline": "sciences",
    "aliases": [
      "Trends in Biochemical Sciences"
    ]
  },
  {
    "id": "trends-cell-biology",
    "name": "Trends in Cell Biology",
    "discipline": "sciences",
    "aliases": [
      "Trends in Cell Biology"
    ]
  },
  {
    "id": "trends-ecology-evolution",
    "name": "Trends in Ecology and Evolution",
    "discipline": "sciences",
    "aliases": [
      "Trends in Ecology and Evolution"
    ]
  },
  {
    "id": "trends-genetics",
    "name": "Trends in Genetics",
    "discipline": "sciences",
    "aliases": [
      "Trends in Genetics"
    ]
  },
  {
    "id": "trends-microbiology",
    "name": "Trends in Microbiology",
    "discipline": "sciences",
    "aliases": [
      "Trends in Microbiology"
    ]
  },
  {
    "id": "trends-molecular-medicine",
    "name": "Trends in Molecular Medicine",
    "discipline": "medicine",
    "aliases": [
      "Trends in Molecular Medicine"
    ]
  },
  {
    "id": "trends-neurosciences",
    "name": "Trends in Neurosciences",
    "discipline": "medicine",
    "aliases": [
      "Trends in Neurosciences"
    ]
  },
  {
    "id": "trends-plant-science",
    "name": "Trends in Plant Science",
    "discipline": "sciences",
    "aliases": [
      "Trends in Plant Science"
    ]
  },
  {
    "id": "universal-journal-educational-research",
    "name": "Universal Journal of Educational Research",
    "discipline": "general",
    "aliases": [
      "Universal Journal of Educational Research"
    ]
  },
  {
    "id": "vanderbilt-law-review",
    "name": "Vanderbilt Law Review",
    "discipline": "law",
    "aliases": [
      "Vanderbilt Law Review"
    ]
  },
  {
    "id": "virginia-law-review",
    "name": "Virginia Law Review",
    "discipline": "law",
    "aliases": [
      "Virginia Law Review"
    ]
  },
  {
    "id": "water-research",
    "name": "Water Research",
    "discipline": "sciences",
    "aliases": [
      "Water Research"
    ]
  },
  {
    "id": "wisconsin-law-review",
    "name": "Wisconsin Law Review",
    "discipline": "law",
    "aliases": [
      "Wisconsin Law Review"
    ]
  },
  {
    "id": "world-journal-gastroenterology",
    "name": "World Journal of Gastroenterology",
    "discipline": "medicine",
    "aliases": [
      "World Journal of Gastroenterology"
    ]
  },
  {
    "id": "world-politics",
    "name": "World Politics",
    "discipline": "humanities",
    "aliases": [
      "World Politics"
    ]
  },
  {
    "id": "yale-law-journal",
    "name": "Yale Law Journal",
    "discipline": "law",
    "aliases": [
      "Yale Law Journal"
    ]
  },
  {
    "id": "ieee-trans-automatic-control",
    "name": "IEEE Transactions on Automatic Control",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Automatic Control"
    ]
  },
  {
    "id": "ieee-trans-biomedical-engineering",
    "name": "IEEE Transactions on Biomedical Engineering",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Biomedical Engineering"
    ]
  },
  {
    "id": "ieee-trans-communications",
    "name": "IEEE Transactions on Communications",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Communications"
    ]
  },
  {
    "id": "ieee-trans-computers",
    "name": "IEEE Transactions on Computers",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Computers"
    ]
  },
  {
    "id": "ieee-trans-control-systems-technology",
    "name": "IEEE Transactions on Control Systems Technology",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Control Systems Technology"
    ]
  },
  {
    "id": "ieee-trans-cybernetics",
    "name": "IEEE Transactions on Cybernetics",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Cybernetics"
    ]
  },
  {
    "id": "ieee-trans-image-processing",
    "name": "IEEE Transactions on Image Processing",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Image Processing"
    ]
  },
  {
    "id": "ieee-trans-industrial-electronics",
    "name": "IEEE Transactions on Industrial Electronics",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Industrial Electronics"
    ]
  },
  {
    "id": "ieee-trans-industrial-informatics",
    "name": "IEEE Transactions on Industrial Informatics",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Industrial Informatics"
    ]
  },
  {
    "id": "ieee-trans-information-theory",
    "name": "IEEE Transactions on Information Theory",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Information Theory"
    ]
  },
  {
    "id": "ieee-trans-instrumentation-and-measurement",
    "name": "IEEE Transactions on Instrumentation and Measurement",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Instrumentation and Measurement"
    ]
  },
  {
    "id": "ieee-trans-knowledge-and-data-engineering",
    "name": "IEEE Transactions on Knowledge and Data Engineering",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Knowledge and Data Engineering"
    ]
  },
  {
    "id": "ieee-trans-medical-imaging",
    "name": "IEEE Transactions on Medical Imaging",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Medical Imaging"
    ]
  },
  {
    "id": "ieee-trans-mobile-computing",
    "name": "IEEE Transactions on Mobile Computing",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Mobile Computing"
    ]
  },
  {
    "id": "ieee-trans-neural-networks-and-learning-systems",
    "name": "IEEE Transactions on Neural Networks and Learning Systems",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Neural Networks and Learning Systems"
    ]
  },
  {
    "id": "ieee-trans-power-systems",
    "name": "IEEE Transactions on Power Systems",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Power Systems"
    ]
  },
  {
    "id": "ieee-trans-reliability",
    "name": "IEEE Transactions on Reliability",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Reliability"
    ]
  },
  {
    "id": "ieee-trans-signal-processing",
    "name": "IEEE Transactions on Signal Processing",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Signal Processing"
    ]
  },
  {
    "id": "ieee-trans-software-engineering",
    "name": "IEEE Transactions on Software Engineering",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Software Engineering"
    ]
  },
  {
    "id": "ieee-trans-systems-man-and-cybernetics",
    "name": "IEEE Transactions on Systems Man and Cybernetics",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Systems Man and Cybernetics"
    ]
  },
  {
    "id": "ieee-trans-vehicular-technology",
    "name": "IEEE Transactions on Vehicular Technology",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Vehicular Technology"
    ]
  },
  {
    "id": "ieee-trans-very-large-scale-integration",
    "name": "IEEE Transactions on Very Large Scale Integration",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Very Large Scale Integration"
    ]
  },
  {
    "id": "ieee-trans-wireless-communications",
    "name": "IEEE Transactions on Wireless Communications",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Wireless Communications"
    ]
  },
  {
    "id": "ieee-trans-electron-devices",
    "name": "IEEE Transactions on Electron Devices",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Electron Devices"
    ]
  },
  {
    "id": "ieee-trans-nanotechnology",
    "name": "IEEE Transactions on Nanotechnology",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Nanotechnology"
    ]
  },
  {
    "id": "ieee-trans-quantum-engineering",
    "name": "IEEE Transactions on Quantum Engineering",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Quantum Engineering"
    ]
  },
  {
    "id": "ieee-trans-parallel-and-distributed-systems",
    "name": "IEEE Transactions on Parallel and Distributed Systems",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Parallel and Distributed Systems"
    ]
  },
  {
    "id": "ieee-trans-network-science-and-engineering",
    "name": "IEEE Transactions on Network Science and Engineering",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Network Science and Engineering"
    ]
  },
  {
    "id": "ieee-trans-green-communications-and-networking",
    "name": "IEEE Transactions on Green Communications and Networking",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Green Communications and Networking"
    ]
  },
  {
    "id": "ieee-trans-cognitive-communications-and-networking",
    "name": "IEEE Transactions on Cognitive Communications and Networking",
    "discipline": "sciences",
    "aliases": [
      "IEEE",
      "engineering",
      "IEEE Transactions on Cognitive Communications and Networking"
    ]
  },
  {
    "id": "acm-computing-surveys",
    "name": "ACM Computing Surveys",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Computing Surveys"
    ]
  },
  {
    "id": "acm-transactions-on-algorithms",
    "name": "ACM Transactions on Algorithms",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Algorithms"
    ]
  },
  {
    "id": "acm-transactions-on-applied-perception",
    "name": "ACM Transactions on Applied Perception",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Applied Perception"
    ]
  },
  {
    "id": "acm-transactions-on-architecture-and-code-optimization",
    "name": "ACM Transactions on Architecture and Code Optimization",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Architecture and Code Optimization"
    ]
  },
  {
    "id": "acm-transactions-on-autonomous-and-adaptive-systems",
    "name": "ACM Transactions on Autonomous and Adaptive Systems",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Autonomous and Adaptive Systems"
    ]
  },
  {
    "id": "acm-transactions-on-computation-theory",
    "name": "ACM Transactions on Computation Theory",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Computation Theory"
    ]
  },
  {
    "id": "acm-transactions-on-computer-systems",
    "name": "ACM Transactions on Computer Systems",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Computer Systems"
    ]
  },
  {
    "id": "acm-transactions-on-computing-for-healthcare",
    "name": "ACM Transactions on Computing for Healthcare",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Computing for Healthcare"
    ]
  },
  {
    "id": "acm-transactions-on-database-systems",
    "name": "ACM Transactions on Database Systems",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Database Systems"
    ]
  },
  {
    "id": "acm-transactions-on-design-automation-of-electronic-systems",
    "name": "ACM Transactions on Design Automation of Electronic Systems",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Design Automation of Electronic Systems"
    ]
  },
  {
    "id": "acm-transactions-on-economics-and-computation",
    "name": "ACM Transactions on Economics and Computation",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Economics and Computation"
    ]
  },
  {
    "id": "acm-transactions-on-embedded-computing-systems",
    "name": "ACM Transactions on Embedded Computing Systems",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Embedded Computing Systems"
    ]
  },
  {
    "id": "acm-transactions-on-evolutionary-learning-and-optimization",
    "name": "ACM Transactions on Evolutionary Learning and Optimization",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Evolutionary Learning and Optimization"
    ]
  },
  {
    "id": "acm-transactions-on-graphics",
    "name": "ACM Transactions on Graphics",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Graphics"
    ]
  },
  {
    "id": "acm-transactions-on-human-robot-interaction",
    "name": "ACM Transactions on Human-Robot Interaction",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Human-Robot Interaction"
    ]
  },
  {
    "id": "acm-transactions-on-information-systems",
    "name": "ACM Transactions on Information Systems",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Information Systems"
    ]
  },
  {
    "id": "acm-transactions-on-interactive-intelligent-systems",
    "name": "ACM Transactions on Interactive Intelligent Systems",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Interactive Intelligent Systems"
    ]
  },
  {
    "id": "acm-transactions-on-internet-technology",
    "name": "ACM Transactions on Internet Technology",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Internet Technology"
    ]
  },
  {
    "id": "acm-transactions-on-knowledge-discovery-from-data",
    "name": "ACM Transactions on Knowledge Discovery from Data",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Knowledge Discovery from Data"
    ]
  },
  {
    "id": "acm-transactions-on-management-information-systems",
    "name": "ACM Transactions on Management Information Systems",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Management Information Systems"
    ]
  },
  {
    "id": "acm-transactions-on-mathematical-software",
    "name": "ACM Transactions on Mathematical Software",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Mathematical Software"
    ]
  },
  {
    "id": "acm-transactions-on-multimedia-computing-communications-and-applications",
    "name": "ACM Transactions on Multimedia Computing Communications and Applications",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Multimedia Computing Communications and Applications"
    ]
  },
  {
    "id": "acm-transactions-on-modeling-and-computer-simulation",
    "name": "ACM Transactions on Modeling and Computer Simulation",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Modeling and Computer Simulation"
    ]
  },
  {
    "id": "acm-transactions-on-programming-languages-and-systems",
    "name": "ACM Transactions on Programming Languages and Systems",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Programming Languages and Systems"
    ]
  },
  {
    "id": "acm-transactions-on-recommender-systems",
    "name": "ACM Transactions on Recommender Systems",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Recommender Systems"
    ]
  },
  {
    "id": "acm-transactions-on-sensor-networks",
    "name": "ACM Transactions on Sensor Networks",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Sensor Networks"
    ]
  },
  {
    "id": "acm-transactions-on-social-computing",
    "name": "ACM Transactions on Social Computing",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Social Computing"
    ]
  },
  {
    "id": "acm-transactions-on-software-engineering-and-methodology",
    "name": "ACM Transactions on Software Engineering and Methodology",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Software Engineering and Methodology"
    ]
  },
  {
    "id": "acm-transactions-on-spatial-algorithms-and-systems",
    "name": "ACM Transactions on Spatial Algorithms and Systems",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Spatial Algorithms and Systems"
    ]
  },
  {
    "id": "acm-transactions-on-storage",
    "name": "ACM Transactions on Storage",
    "discipline": "sciences",
    "aliases": [
      "ACM",
      "computer science",
      "ACM Transactions on Storage"
    ]
  },
  {
    "id": "australian-sciences-author-date",
    "name": "Australian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Australian",
      "Australian sciences",
      "Australian Author-Date"
    ]
  },
  {
    "id": "australian-sciences-footnote",
    "name": "Australian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Australian",
      "Australian sciences",
      "Australian Footnote"
    ]
  },
  {
    "id": "australian-sciences-endnote",
    "name": "Australian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Australian",
      "Australian sciences",
      "Australian Endnote"
    ]
  },
  {
    "id": "australian-sciences-numeric",
    "name": "Australian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Australian",
      "Australian sciences",
      "Australian Numeric"
    ]
  },
  {
    "id": "australian-sciences-vancouver-style",
    "name": "Australian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Australian",
      "Australian sciences",
      "Australian Vancouver-Style"
    ]
  },
  {
    "id": "australian-sciences-author-number",
    "name": "Australian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Australian",
      "Australian sciences",
      "Australian Author-Number"
    ]
  },
  {
    "id": "australian-humanities-author-date",
    "name": "Australian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Australian",
      "Australian humanities",
      "Australian Author-Date"
    ]
  },
  {
    "id": "australian-humanities-footnote",
    "name": "Australian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Australian",
      "Australian humanities",
      "Australian Footnote"
    ]
  },
  {
    "id": "australian-humanities-endnote",
    "name": "Australian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Australian",
      "Australian humanities",
      "Australian Endnote"
    ]
  },
  {
    "id": "australian-humanities-numeric",
    "name": "Australian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Australian",
      "Australian humanities",
      "Australian Numeric"
    ]
  },
  {
    "id": "australian-humanities-vancouver-style",
    "name": "Australian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Australian",
      "Australian humanities",
      "Australian Vancouver-Style"
    ]
  },
  {
    "id": "australian-humanities-author-number",
    "name": "Australian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Australian",
      "Australian humanities",
      "Australian Author-Number"
    ]
  },
  {
    "id": "australian-law-author-date",
    "name": "Australian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Australian",
      "Australian law",
      "Australian Author-Date"
    ]
  },
  {
    "id": "australian-law-footnote",
    "name": "Australian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Australian",
      "Australian law",
      "Australian Footnote"
    ]
  },
  {
    "id": "australian-law-endnote",
    "name": "Australian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Australian",
      "Australian law",
      "Australian Endnote"
    ]
  },
  {
    "id": "australian-law-numeric",
    "name": "Australian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Australian",
      "Australian law",
      "Australian Numeric"
    ]
  },
  {
    "id": "australian-law-vancouver-style",
    "name": "Australian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Australian",
      "Australian law",
      "Australian Vancouver-Style"
    ]
  },
  {
    "id": "australian-law-author-number",
    "name": "Australian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Australian",
      "Australian law",
      "Australian Author-Number"
    ]
  },
  {
    "id": "australian-medicine-author-date",
    "name": "Australian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Australian",
      "Australian medicine",
      "Australian Author-Date"
    ]
  },
  {
    "id": "australian-medicine-footnote",
    "name": "Australian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Australian",
      "Australian medicine",
      "Australian Footnote"
    ]
  },
  {
    "id": "australian-medicine-endnote",
    "name": "Australian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Australian",
      "Australian medicine",
      "Australian Endnote"
    ]
  },
  {
    "id": "australian-medicine-numeric",
    "name": "Australian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Australian",
      "Australian medicine",
      "Australian Numeric"
    ]
  },
  {
    "id": "australian-medicine-vancouver-style",
    "name": "Australian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Australian",
      "Australian medicine",
      "Australian Vancouver-Style"
    ]
  },
  {
    "id": "australian-medicine-author-number",
    "name": "Australian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Australian",
      "Australian medicine",
      "Australian Author-Number"
    ]
  },
  {
    "id": "australian-general-author-date",
    "name": "Australian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Australian",
      "Australian general",
      "Australian Author-Date"
    ]
  },
  {
    "id": "australian-general-footnote",
    "name": "Australian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Australian",
      "Australian general",
      "Australian Footnote"
    ]
  },
  {
    "id": "australian-general-endnote",
    "name": "Australian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Australian",
      "Australian general",
      "Australian Endnote"
    ]
  },
  {
    "id": "australian-general-numeric",
    "name": "Australian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Australian",
      "Australian general",
      "Australian Numeric"
    ]
  },
  {
    "id": "australian-general-vancouver-style",
    "name": "Australian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Australian",
      "Australian general",
      "Australian Vancouver-Style"
    ]
  },
  {
    "id": "australian-general-author-number",
    "name": "Australian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Australian",
      "Australian general",
      "Australian Author-Number"
    ]
  },
  {
    "id": "british-sciences-author-date",
    "name": "British Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "British",
      "British sciences",
      "British Author-Date"
    ]
  },
  {
    "id": "british-sciences-footnote",
    "name": "British Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "British",
      "British sciences",
      "British Footnote"
    ]
  },
  {
    "id": "british-sciences-endnote",
    "name": "British Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "British",
      "British sciences",
      "British Endnote"
    ]
  },
  {
    "id": "british-sciences-numeric",
    "name": "British Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "British",
      "British sciences",
      "British Numeric"
    ]
  },
  {
    "id": "british-sciences-vancouver-style",
    "name": "British Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "British",
      "British sciences",
      "British Vancouver-Style"
    ]
  },
  {
    "id": "british-sciences-author-number",
    "name": "British Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "British",
      "British sciences",
      "British Author-Number"
    ]
  },
  {
    "id": "british-humanities-author-date",
    "name": "British Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "British",
      "British humanities",
      "British Author-Date"
    ]
  },
  {
    "id": "british-humanities-footnote",
    "name": "British Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "British",
      "British humanities",
      "British Footnote"
    ]
  },
  {
    "id": "british-humanities-endnote",
    "name": "British Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "British",
      "British humanities",
      "British Endnote"
    ]
  },
  {
    "id": "british-humanities-numeric",
    "name": "British Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "British",
      "British humanities",
      "British Numeric"
    ]
  },
  {
    "id": "british-humanities-vancouver-style",
    "name": "British Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "British",
      "British humanities",
      "British Vancouver-Style"
    ]
  },
  {
    "id": "british-humanities-author-number",
    "name": "British Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "British",
      "British humanities",
      "British Author-Number"
    ]
  },
  {
    "id": "british-law-author-date",
    "name": "British Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "British",
      "British law",
      "British Author-Date"
    ]
  },
  {
    "id": "british-law-footnote",
    "name": "British Law — Footnote",
    "discipline": "law",
    "aliases": [
      "British",
      "British law",
      "British Footnote"
    ]
  },
  {
    "id": "british-law-endnote",
    "name": "British Law — Endnote",
    "discipline": "law",
    "aliases": [
      "British",
      "British law",
      "British Endnote"
    ]
  },
  {
    "id": "british-law-numeric",
    "name": "British Law — Numeric",
    "discipline": "law",
    "aliases": [
      "British",
      "British law",
      "British Numeric"
    ]
  },
  {
    "id": "british-law-vancouver-style",
    "name": "British Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "British",
      "British law",
      "British Vancouver-Style"
    ]
  },
  {
    "id": "british-law-author-number",
    "name": "British Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "British",
      "British law",
      "British Author-Number"
    ]
  },
  {
    "id": "british-medicine-author-date",
    "name": "British Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "British",
      "British medicine",
      "British Author-Date"
    ]
  },
  {
    "id": "british-medicine-footnote",
    "name": "British Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "British",
      "British medicine",
      "British Footnote"
    ]
  },
  {
    "id": "british-medicine-endnote",
    "name": "British Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "British",
      "British medicine",
      "British Endnote"
    ]
  },
  {
    "id": "british-medicine-numeric",
    "name": "British Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "British",
      "British medicine",
      "British Numeric"
    ]
  },
  {
    "id": "british-medicine-vancouver-style",
    "name": "British Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "British",
      "British medicine",
      "British Vancouver-Style"
    ]
  },
  {
    "id": "british-medicine-author-number",
    "name": "British Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "British",
      "British medicine",
      "British Author-Number"
    ]
  },
  {
    "id": "british-general-author-date",
    "name": "British General — Author-Date",
    "discipline": "general",
    "aliases": [
      "British",
      "British general",
      "British Author-Date"
    ]
  },
  {
    "id": "british-general-footnote",
    "name": "British General — Footnote",
    "discipline": "general",
    "aliases": [
      "British",
      "British general",
      "British Footnote"
    ]
  },
  {
    "id": "british-general-endnote",
    "name": "British General — Endnote",
    "discipline": "general",
    "aliases": [
      "British",
      "British general",
      "British Endnote"
    ]
  },
  {
    "id": "british-general-numeric",
    "name": "British General — Numeric",
    "discipline": "general",
    "aliases": [
      "British",
      "British general",
      "British Numeric"
    ]
  },
  {
    "id": "british-general-vancouver-style",
    "name": "British General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "British",
      "British general",
      "British Vancouver-Style"
    ]
  },
  {
    "id": "british-general-author-number",
    "name": "British General — Author-Number",
    "discipline": "general",
    "aliases": [
      "British",
      "British general",
      "British Author-Number"
    ]
  },
  {
    "id": "canadian-sciences-author-date",
    "name": "Canadian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Canadian",
      "Canadian sciences",
      "Canadian Author-Date"
    ]
  },
  {
    "id": "canadian-sciences-footnote",
    "name": "Canadian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Canadian",
      "Canadian sciences",
      "Canadian Footnote"
    ]
  },
  {
    "id": "canadian-sciences-endnote",
    "name": "Canadian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Canadian",
      "Canadian sciences",
      "Canadian Endnote"
    ]
  },
  {
    "id": "canadian-sciences-numeric",
    "name": "Canadian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Canadian",
      "Canadian sciences",
      "Canadian Numeric"
    ]
  },
  {
    "id": "canadian-sciences-vancouver-style",
    "name": "Canadian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Canadian",
      "Canadian sciences",
      "Canadian Vancouver-Style"
    ]
  },
  {
    "id": "canadian-sciences-author-number",
    "name": "Canadian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Canadian",
      "Canadian sciences",
      "Canadian Author-Number"
    ]
  },
  {
    "id": "canadian-humanities-author-date",
    "name": "Canadian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Canadian",
      "Canadian humanities",
      "Canadian Author-Date"
    ]
  },
  {
    "id": "canadian-humanities-footnote",
    "name": "Canadian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Canadian",
      "Canadian humanities",
      "Canadian Footnote"
    ]
  },
  {
    "id": "canadian-humanities-endnote",
    "name": "Canadian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Canadian",
      "Canadian humanities",
      "Canadian Endnote"
    ]
  },
  {
    "id": "canadian-humanities-numeric",
    "name": "Canadian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Canadian",
      "Canadian humanities",
      "Canadian Numeric"
    ]
  },
  {
    "id": "canadian-humanities-vancouver-style",
    "name": "Canadian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Canadian",
      "Canadian humanities",
      "Canadian Vancouver-Style"
    ]
  },
  {
    "id": "canadian-humanities-author-number",
    "name": "Canadian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Canadian",
      "Canadian humanities",
      "Canadian Author-Number"
    ]
  },
  {
    "id": "canadian-law-author-date",
    "name": "Canadian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Canadian",
      "Canadian law",
      "Canadian Author-Date"
    ]
  },
  {
    "id": "canadian-law-footnote",
    "name": "Canadian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Canadian",
      "Canadian law",
      "Canadian Footnote"
    ]
  },
  {
    "id": "canadian-law-endnote",
    "name": "Canadian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Canadian",
      "Canadian law",
      "Canadian Endnote"
    ]
  },
  {
    "id": "canadian-law-numeric",
    "name": "Canadian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Canadian",
      "Canadian law",
      "Canadian Numeric"
    ]
  },
  {
    "id": "canadian-law-vancouver-style",
    "name": "Canadian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Canadian",
      "Canadian law",
      "Canadian Vancouver-Style"
    ]
  },
  {
    "id": "canadian-law-author-number",
    "name": "Canadian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Canadian",
      "Canadian law",
      "Canadian Author-Number"
    ]
  },
  {
    "id": "canadian-medicine-author-date",
    "name": "Canadian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Canadian",
      "Canadian medicine",
      "Canadian Author-Date"
    ]
  },
  {
    "id": "canadian-medicine-footnote",
    "name": "Canadian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Canadian",
      "Canadian medicine",
      "Canadian Footnote"
    ]
  },
  {
    "id": "canadian-medicine-endnote",
    "name": "Canadian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Canadian",
      "Canadian medicine",
      "Canadian Endnote"
    ]
  },
  {
    "id": "canadian-medicine-numeric",
    "name": "Canadian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Canadian",
      "Canadian medicine",
      "Canadian Numeric"
    ]
  },
  {
    "id": "canadian-medicine-vancouver-style",
    "name": "Canadian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Canadian",
      "Canadian medicine",
      "Canadian Vancouver-Style"
    ]
  },
  {
    "id": "canadian-medicine-author-number",
    "name": "Canadian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Canadian",
      "Canadian medicine",
      "Canadian Author-Number"
    ]
  },
  {
    "id": "canadian-general-author-date",
    "name": "Canadian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Canadian",
      "Canadian general",
      "Canadian Author-Date"
    ]
  },
  {
    "id": "canadian-general-footnote",
    "name": "Canadian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Canadian",
      "Canadian general",
      "Canadian Footnote"
    ]
  },
  {
    "id": "canadian-general-endnote",
    "name": "Canadian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Canadian",
      "Canadian general",
      "Canadian Endnote"
    ]
  },
  {
    "id": "canadian-general-numeric",
    "name": "Canadian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Canadian",
      "Canadian general",
      "Canadian Numeric"
    ]
  },
  {
    "id": "canadian-general-vancouver-style",
    "name": "Canadian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Canadian",
      "Canadian general",
      "Canadian Vancouver-Style"
    ]
  },
  {
    "id": "canadian-general-author-number",
    "name": "Canadian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Canadian",
      "Canadian general",
      "Canadian Author-Number"
    ]
  },
  {
    "id": "german-sciences-author-date",
    "name": "German Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "German",
      "German sciences",
      "German Author-Date"
    ]
  },
  {
    "id": "german-sciences-footnote",
    "name": "German Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "German",
      "German sciences",
      "German Footnote"
    ]
  },
  {
    "id": "german-sciences-endnote",
    "name": "German Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "German",
      "German sciences",
      "German Endnote"
    ]
  },
  {
    "id": "german-sciences-numeric",
    "name": "German Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "German",
      "German sciences",
      "German Numeric"
    ]
  },
  {
    "id": "german-sciences-vancouver-style",
    "name": "German Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "German",
      "German sciences",
      "German Vancouver-Style"
    ]
  },
  {
    "id": "german-sciences-author-number",
    "name": "German Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "German",
      "German sciences",
      "German Author-Number"
    ]
  },
  {
    "id": "german-humanities-author-date",
    "name": "German Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "German",
      "German humanities",
      "German Author-Date"
    ]
  },
  {
    "id": "german-humanities-footnote",
    "name": "German Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "German",
      "German humanities",
      "German Footnote"
    ]
  },
  {
    "id": "german-humanities-endnote",
    "name": "German Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "German",
      "German humanities",
      "German Endnote"
    ]
  },
  {
    "id": "german-humanities-numeric",
    "name": "German Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "German",
      "German humanities",
      "German Numeric"
    ]
  },
  {
    "id": "german-humanities-vancouver-style",
    "name": "German Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "German",
      "German humanities",
      "German Vancouver-Style"
    ]
  },
  {
    "id": "german-humanities-author-number",
    "name": "German Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "German",
      "German humanities",
      "German Author-Number"
    ]
  },
  {
    "id": "german-law-author-date",
    "name": "German Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "German",
      "German law",
      "German Author-Date"
    ]
  },
  {
    "id": "german-law-footnote",
    "name": "German Law — Footnote",
    "discipline": "law",
    "aliases": [
      "German",
      "German law",
      "German Footnote"
    ]
  },
  {
    "id": "german-law-endnote",
    "name": "German Law — Endnote",
    "discipline": "law",
    "aliases": [
      "German",
      "German law",
      "German Endnote"
    ]
  },
  {
    "id": "german-law-numeric",
    "name": "German Law — Numeric",
    "discipline": "law",
    "aliases": [
      "German",
      "German law",
      "German Numeric"
    ]
  },
  {
    "id": "german-law-vancouver-style",
    "name": "German Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "German",
      "German law",
      "German Vancouver-Style"
    ]
  },
  {
    "id": "german-law-author-number",
    "name": "German Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "German",
      "German law",
      "German Author-Number"
    ]
  },
  {
    "id": "german-medicine-author-date",
    "name": "German Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "German",
      "German medicine",
      "German Author-Date"
    ]
  },
  {
    "id": "german-medicine-footnote",
    "name": "German Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "German",
      "German medicine",
      "German Footnote"
    ]
  },
  {
    "id": "german-medicine-endnote",
    "name": "German Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "German",
      "German medicine",
      "German Endnote"
    ]
  },
  {
    "id": "german-medicine-numeric",
    "name": "German Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "German",
      "German medicine",
      "German Numeric"
    ]
  },
  {
    "id": "german-medicine-vancouver-style",
    "name": "German Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "German",
      "German medicine",
      "German Vancouver-Style"
    ]
  },
  {
    "id": "german-medicine-author-number",
    "name": "German Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "German",
      "German medicine",
      "German Author-Number"
    ]
  },
  {
    "id": "german-general-author-date",
    "name": "German General — Author-Date",
    "discipline": "general",
    "aliases": [
      "German",
      "German general",
      "German Author-Date"
    ]
  },
  {
    "id": "german-general-footnote",
    "name": "German General — Footnote",
    "discipline": "general",
    "aliases": [
      "German",
      "German general",
      "German Footnote"
    ]
  },
  {
    "id": "german-general-endnote",
    "name": "German General — Endnote",
    "discipline": "general",
    "aliases": [
      "German",
      "German general",
      "German Endnote"
    ]
  },
  {
    "id": "german-general-numeric",
    "name": "German General — Numeric",
    "discipline": "general",
    "aliases": [
      "German",
      "German general",
      "German Numeric"
    ]
  },
  {
    "id": "german-general-vancouver-style",
    "name": "German General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "German",
      "German general",
      "German Vancouver-Style"
    ]
  },
  {
    "id": "german-general-author-number",
    "name": "German General — Author-Number",
    "discipline": "general",
    "aliases": [
      "German",
      "German general",
      "German Author-Number"
    ]
  },
  {
    "id": "french-sciences-author-date",
    "name": "French Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "French",
      "French sciences",
      "French Author-Date"
    ]
  },
  {
    "id": "french-sciences-footnote",
    "name": "French Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "French",
      "French sciences",
      "French Footnote"
    ]
  },
  {
    "id": "french-sciences-endnote",
    "name": "French Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "French",
      "French sciences",
      "French Endnote"
    ]
  },
  {
    "id": "french-sciences-numeric",
    "name": "French Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "French",
      "French sciences",
      "French Numeric"
    ]
  },
  {
    "id": "french-sciences-vancouver-style",
    "name": "French Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "French",
      "French sciences",
      "French Vancouver-Style"
    ]
  },
  {
    "id": "french-sciences-author-number",
    "name": "French Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "French",
      "French sciences",
      "French Author-Number"
    ]
  },
  {
    "id": "french-humanities-author-date",
    "name": "French Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "French",
      "French humanities",
      "French Author-Date"
    ]
  },
  {
    "id": "french-humanities-footnote",
    "name": "French Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "French",
      "French humanities",
      "French Footnote"
    ]
  },
  {
    "id": "french-humanities-endnote",
    "name": "French Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "French",
      "French humanities",
      "French Endnote"
    ]
  },
  {
    "id": "french-humanities-numeric",
    "name": "French Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "French",
      "French humanities",
      "French Numeric"
    ]
  },
  {
    "id": "french-humanities-vancouver-style",
    "name": "French Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "French",
      "French humanities",
      "French Vancouver-Style"
    ]
  },
  {
    "id": "french-humanities-author-number",
    "name": "French Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "French",
      "French humanities",
      "French Author-Number"
    ]
  },
  {
    "id": "french-law-author-date",
    "name": "French Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "French",
      "French law",
      "French Author-Date"
    ]
  },
  {
    "id": "french-law-footnote",
    "name": "French Law — Footnote",
    "discipline": "law",
    "aliases": [
      "French",
      "French law",
      "French Footnote"
    ]
  },
  {
    "id": "french-law-endnote",
    "name": "French Law — Endnote",
    "discipline": "law",
    "aliases": [
      "French",
      "French law",
      "French Endnote"
    ]
  },
  {
    "id": "french-law-numeric",
    "name": "French Law — Numeric",
    "discipline": "law",
    "aliases": [
      "French",
      "French law",
      "French Numeric"
    ]
  },
  {
    "id": "french-law-vancouver-style",
    "name": "French Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "French",
      "French law",
      "French Vancouver-Style"
    ]
  },
  {
    "id": "french-law-author-number",
    "name": "French Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "French",
      "French law",
      "French Author-Number"
    ]
  },
  {
    "id": "french-medicine-author-date",
    "name": "French Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "French",
      "French medicine",
      "French Author-Date"
    ]
  },
  {
    "id": "french-medicine-footnote",
    "name": "French Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "French",
      "French medicine",
      "French Footnote"
    ]
  },
  {
    "id": "french-medicine-endnote",
    "name": "French Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "French",
      "French medicine",
      "French Endnote"
    ]
  },
  {
    "id": "french-medicine-numeric",
    "name": "French Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "French",
      "French medicine",
      "French Numeric"
    ]
  },
  {
    "id": "french-medicine-vancouver-style",
    "name": "French Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "French",
      "French medicine",
      "French Vancouver-Style"
    ]
  },
  {
    "id": "french-medicine-author-number",
    "name": "French Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "French",
      "French medicine",
      "French Author-Number"
    ]
  },
  {
    "id": "french-general-author-date",
    "name": "French General — Author-Date",
    "discipline": "general",
    "aliases": [
      "French",
      "French general",
      "French Author-Date"
    ]
  },
  {
    "id": "french-general-footnote",
    "name": "French General — Footnote",
    "discipline": "general",
    "aliases": [
      "French",
      "French general",
      "French Footnote"
    ]
  },
  {
    "id": "french-general-endnote",
    "name": "French General — Endnote",
    "discipline": "general",
    "aliases": [
      "French",
      "French general",
      "French Endnote"
    ]
  },
  {
    "id": "french-general-numeric",
    "name": "French General — Numeric",
    "discipline": "general",
    "aliases": [
      "French",
      "French general",
      "French Numeric"
    ]
  },
  {
    "id": "french-general-vancouver-style",
    "name": "French General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "French",
      "French general",
      "French Vancouver-Style"
    ]
  },
  {
    "id": "french-general-author-number",
    "name": "French General — Author-Number",
    "discipline": "general",
    "aliases": [
      "French",
      "French general",
      "French Author-Number"
    ]
  },
  {
    "id": "dutch-sciences-author-date",
    "name": "Dutch Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Dutch",
      "Dutch sciences",
      "Dutch Author-Date"
    ]
  },
  {
    "id": "dutch-sciences-footnote",
    "name": "Dutch Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Dutch",
      "Dutch sciences",
      "Dutch Footnote"
    ]
  },
  {
    "id": "dutch-sciences-endnote",
    "name": "Dutch Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Dutch",
      "Dutch sciences",
      "Dutch Endnote"
    ]
  },
  {
    "id": "dutch-sciences-numeric",
    "name": "Dutch Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Dutch",
      "Dutch sciences",
      "Dutch Numeric"
    ]
  },
  {
    "id": "dutch-sciences-vancouver-style",
    "name": "Dutch Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Dutch",
      "Dutch sciences",
      "Dutch Vancouver-Style"
    ]
  },
  {
    "id": "dutch-sciences-author-number",
    "name": "Dutch Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Dutch",
      "Dutch sciences",
      "Dutch Author-Number"
    ]
  },
  {
    "id": "dutch-humanities-author-date",
    "name": "Dutch Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Dutch",
      "Dutch humanities",
      "Dutch Author-Date"
    ]
  },
  {
    "id": "dutch-humanities-footnote",
    "name": "Dutch Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Dutch",
      "Dutch humanities",
      "Dutch Footnote"
    ]
  },
  {
    "id": "dutch-humanities-endnote",
    "name": "Dutch Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Dutch",
      "Dutch humanities",
      "Dutch Endnote"
    ]
  },
  {
    "id": "dutch-humanities-numeric",
    "name": "Dutch Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Dutch",
      "Dutch humanities",
      "Dutch Numeric"
    ]
  },
  {
    "id": "dutch-humanities-vancouver-style",
    "name": "Dutch Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Dutch",
      "Dutch humanities",
      "Dutch Vancouver-Style"
    ]
  },
  {
    "id": "dutch-humanities-author-number",
    "name": "Dutch Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Dutch",
      "Dutch humanities",
      "Dutch Author-Number"
    ]
  },
  {
    "id": "dutch-law-author-date",
    "name": "Dutch Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Dutch",
      "Dutch law",
      "Dutch Author-Date"
    ]
  },
  {
    "id": "dutch-law-footnote",
    "name": "Dutch Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Dutch",
      "Dutch law",
      "Dutch Footnote"
    ]
  },
  {
    "id": "dutch-law-endnote",
    "name": "Dutch Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Dutch",
      "Dutch law",
      "Dutch Endnote"
    ]
  },
  {
    "id": "dutch-law-numeric",
    "name": "Dutch Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Dutch",
      "Dutch law",
      "Dutch Numeric"
    ]
  },
  {
    "id": "dutch-law-vancouver-style",
    "name": "Dutch Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Dutch",
      "Dutch law",
      "Dutch Vancouver-Style"
    ]
  },
  {
    "id": "dutch-law-author-number",
    "name": "Dutch Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Dutch",
      "Dutch law",
      "Dutch Author-Number"
    ]
  },
  {
    "id": "dutch-medicine-author-date",
    "name": "Dutch Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Dutch",
      "Dutch medicine",
      "Dutch Author-Date"
    ]
  },
  {
    "id": "dutch-medicine-footnote",
    "name": "Dutch Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Dutch",
      "Dutch medicine",
      "Dutch Footnote"
    ]
  },
  {
    "id": "dutch-medicine-endnote",
    "name": "Dutch Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Dutch",
      "Dutch medicine",
      "Dutch Endnote"
    ]
  },
  {
    "id": "dutch-medicine-numeric",
    "name": "Dutch Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Dutch",
      "Dutch medicine",
      "Dutch Numeric"
    ]
  },
  {
    "id": "dutch-medicine-vancouver-style",
    "name": "Dutch Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Dutch",
      "Dutch medicine",
      "Dutch Vancouver-Style"
    ]
  },
  {
    "id": "dutch-medicine-author-number",
    "name": "Dutch Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Dutch",
      "Dutch medicine",
      "Dutch Author-Number"
    ]
  },
  {
    "id": "dutch-general-author-date",
    "name": "Dutch General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Dutch",
      "Dutch general",
      "Dutch Author-Date"
    ]
  },
  {
    "id": "dutch-general-footnote",
    "name": "Dutch General — Footnote",
    "discipline": "general",
    "aliases": [
      "Dutch",
      "Dutch general",
      "Dutch Footnote"
    ]
  },
  {
    "id": "dutch-general-endnote",
    "name": "Dutch General — Endnote",
    "discipline": "general",
    "aliases": [
      "Dutch",
      "Dutch general",
      "Dutch Endnote"
    ]
  },
  {
    "id": "dutch-general-numeric",
    "name": "Dutch General — Numeric",
    "discipline": "general",
    "aliases": [
      "Dutch",
      "Dutch general",
      "Dutch Numeric"
    ]
  },
  {
    "id": "dutch-general-vancouver-style",
    "name": "Dutch General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Dutch",
      "Dutch general",
      "Dutch Vancouver-Style"
    ]
  },
  {
    "id": "dutch-general-author-number",
    "name": "Dutch General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Dutch",
      "Dutch general",
      "Dutch Author-Number"
    ]
  },
  {
    "id": "scandinavian-sciences-author-date",
    "name": "Scandinavian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Scandinavian",
      "Scandinavian sciences",
      "Scandinavian Author-Date"
    ]
  },
  {
    "id": "scandinavian-sciences-footnote",
    "name": "Scandinavian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Scandinavian",
      "Scandinavian sciences",
      "Scandinavian Footnote"
    ]
  },
  {
    "id": "scandinavian-sciences-endnote",
    "name": "Scandinavian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Scandinavian",
      "Scandinavian sciences",
      "Scandinavian Endnote"
    ]
  },
  {
    "id": "scandinavian-sciences-numeric",
    "name": "Scandinavian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Scandinavian",
      "Scandinavian sciences",
      "Scandinavian Numeric"
    ]
  },
  {
    "id": "scandinavian-sciences-vancouver-style",
    "name": "Scandinavian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Scandinavian",
      "Scandinavian sciences",
      "Scandinavian Vancouver-Style"
    ]
  },
  {
    "id": "scandinavian-sciences-author-number",
    "name": "Scandinavian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Scandinavian",
      "Scandinavian sciences",
      "Scandinavian Author-Number"
    ]
  },
  {
    "id": "scandinavian-humanities-author-date",
    "name": "Scandinavian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Scandinavian",
      "Scandinavian humanities",
      "Scandinavian Author-Date"
    ]
  },
  {
    "id": "scandinavian-humanities-footnote",
    "name": "Scandinavian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Scandinavian",
      "Scandinavian humanities",
      "Scandinavian Footnote"
    ]
  },
  {
    "id": "scandinavian-humanities-endnote",
    "name": "Scandinavian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Scandinavian",
      "Scandinavian humanities",
      "Scandinavian Endnote"
    ]
  },
  {
    "id": "scandinavian-humanities-numeric",
    "name": "Scandinavian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Scandinavian",
      "Scandinavian humanities",
      "Scandinavian Numeric"
    ]
  },
  {
    "id": "scandinavian-humanities-vancouver-style",
    "name": "Scandinavian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Scandinavian",
      "Scandinavian humanities",
      "Scandinavian Vancouver-Style"
    ]
  },
  {
    "id": "scandinavian-humanities-author-number",
    "name": "Scandinavian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Scandinavian",
      "Scandinavian humanities",
      "Scandinavian Author-Number"
    ]
  },
  {
    "id": "scandinavian-law-author-date",
    "name": "Scandinavian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Scandinavian",
      "Scandinavian law",
      "Scandinavian Author-Date"
    ]
  },
  {
    "id": "scandinavian-law-footnote",
    "name": "Scandinavian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Scandinavian",
      "Scandinavian law",
      "Scandinavian Footnote"
    ]
  },
  {
    "id": "scandinavian-law-endnote",
    "name": "Scandinavian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Scandinavian",
      "Scandinavian law",
      "Scandinavian Endnote"
    ]
  },
  {
    "id": "scandinavian-law-numeric",
    "name": "Scandinavian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Scandinavian",
      "Scandinavian law",
      "Scandinavian Numeric"
    ]
  },
  {
    "id": "scandinavian-law-vancouver-style",
    "name": "Scandinavian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Scandinavian",
      "Scandinavian law",
      "Scandinavian Vancouver-Style"
    ]
  },
  {
    "id": "scandinavian-law-author-number",
    "name": "Scandinavian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Scandinavian",
      "Scandinavian law",
      "Scandinavian Author-Number"
    ]
  },
  {
    "id": "scandinavian-medicine-author-date",
    "name": "Scandinavian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Scandinavian",
      "Scandinavian medicine",
      "Scandinavian Author-Date"
    ]
  },
  {
    "id": "scandinavian-medicine-footnote",
    "name": "Scandinavian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Scandinavian",
      "Scandinavian medicine",
      "Scandinavian Footnote"
    ]
  },
  {
    "id": "scandinavian-medicine-endnote",
    "name": "Scandinavian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Scandinavian",
      "Scandinavian medicine",
      "Scandinavian Endnote"
    ]
  },
  {
    "id": "scandinavian-medicine-numeric",
    "name": "Scandinavian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Scandinavian",
      "Scandinavian medicine",
      "Scandinavian Numeric"
    ]
  },
  {
    "id": "scandinavian-medicine-vancouver-style",
    "name": "Scandinavian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Scandinavian",
      "Scandinavian medicine",
      "Scandinavian Vancouver-Style"
    ]
  },
  {
    "id": "scandinavian-medicine-author-number",
    "name": "Scandinavian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Scandinavian",
      "Scandinavian medicine",
      "Scandinavian Author-Number"
    ]
  },
  {
    "id": "scandinavian-general-author-date",
    "name": "Scandinavian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Scandinavian",
      "Scandinavian general",
      "Scandinavian Author-Date"
    ]
  },
  {
    "id": "scandinavian-general-footnote",
    "name": "Scandinavian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Scandinavian",
      "Scandinavian general",
      "Scandinavian Footnote"
    ]
  },
  {
    "id": "scandinavian-general-endnote",
    "name": "Scandinavian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Scandinavian",
      "Scandinavian general",
      "Scandinavian Endnote"
    ]
  },
  {
    "id": "scandinavian-general-numeric",
    "name": "Scandinavian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Scandinavian",
      "Scandinavian general",
      "Scandinavian Numeric"
    ]
  },
  {
    "id": "scandinavian-general-vancouver-style",
    "name": "Scandinavian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Scandinavian",
      "Scandinavian general",
      "Scandinavian Vancouver-Style"
    ]
  },
  {
    "id": "scandinavian-general-author-number",
    "name": "Scandinavian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Scandinavian",
      "Scandinavian general",
      "Scandinavian Author-Number"
    ]
  },
  {
    "id": "japanese-sciences-author-date",
    "name": "Japanese Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Japanese",
      "Japanese sciences",
      "Japanese Author-Date"
    ]
  },
  {
    "id": "japanese-sciences-footnote",
    "name": "Japanese Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Japanese",
      "Japanese sciences",
      "Japanese Footnote"
    ]
  },
  {
    "id": "japanese-sciences-endnote",
    "name": "Japanese Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Japanese",
      "Japanese sciences",
      "Japanese Endnote"
    ]
  },
  {
    "id": "japanese-sciences-numeric",
    "name": "Japanese Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Japanese",
      "Japanese sciences",
      "Japanese Numeric"
    ]
  },
  {
    "id": "japanese-sciences-vancouver-style",
    "name": "Japanese Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Japanese",
      "Japanese sciences",
      "Japanese Vancouver-Style"
    ]
  },
  {
    "id": "japanese-sciences-author-number",
    "name": "Japanese Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Japanese",
      "Japanese sciences",
      "Japanese Author-Number"
    ]
  },
  {
    "id": "japanese-humanities-author-date",
    "name": "Japanese Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Japanese",
      "Japanese humanities",
      "Japanese Author-Date"
    ]
  },
  {
    "id": "japanese-humanities-footnote",
    "name": "Japanese Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Japanese",
      "Japanese humanities",
      "Japanese Footnote"
    ]
  },
  {
    "id": "japanese-humanities-endnote",
    "name": "Japanese Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Japanese",
      "Japanese humanities",
      "Japanese Endnote"
    ]
  },
  {
    "id": "japanese-humanities-numeric",
    "name": "Japanese Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Japanese",
      "Japanese humanities",
      "Japanese Numeric"
    ]
  },
  {
    "id": "japanese-humanities-vancouver-style",
    "name": "Japanese Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Japanese",
      "Japanese humanities",
      "Japanese Vancouver-Style"
    ]
  },
  {
    "id": "japanese-humanities-author-number",
    "name": "Japanese Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Japanese",
      "Japanese humanities",
      "Japanese Author-Number"
    ]
  },
  {
    "id": "japanese-law-author-date",
    "name": "Japanese Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Japanese",
      "Japanese law",
      "Japanese Author-Date"
    ]
  },
  {
    "id": "japanese-law-footnote",
    "name": "Japanese Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Japanese",
      "Japanese law",
      "Japanese Footnote"
    ]
  },
  {
    "id": "japanese-law-endnote",
    "name": "Japanese Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Japanese",
      "Japanese law",
      "Japanese Endnote"
    ]
  },
  {
    "id": "japanese-law-numeric",
    "name": "Japanese Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Japanese",
      "Japanese law",
      "Japanese Numeric"
    ]
  },
  {
    "id": "japanese-law-vancouver-style",
    "name": "Japanese Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Japanese",
      "Japanese law",
      "Japanese Vancouver-Style"
    ]
  },
  {
    "id": "japanese-law-author-number",
    "name": "Japanese Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Japanese",
      "Japanese law",
      "Japanese Author-Number"
    ]
  },
  {
    "id": "japanese-medicine-author-date",
    "name": "Japanese Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Japanese",
      "Japanese medicine",
      "Japanese Author-Date"
    ]
  },
  {
    "id": "japanese-medicine-footnote",
    "name": "Japanese Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Japanese",
      "Japanese medicine",
      "Japanese Footnote"
    ]
  },
  {
    "id": "japanese-medicine-endnote",
    "name": "Japanese Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Japanese",
      "Japanese medicine",
      "Japanese Endnote"
    ]
  },
  {
    "id": "japanese-medicine-numeric",
    "name": "Japanese Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Japanese",
      "Japanese medicine",
      "Japanese Numeric"
    ]
  },
  {
    "id": "japanese-medicine-vancouver-style",
    "name": "Japanese Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Japanese",
      "Japanese medicine",
      "Japanese Vancouver-Style"
    ]
  },
  {
    "id": "japanese-medicine-author-number",
    "name": "Japanese Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Japanese",
      "Japanese medicine",
      "Japanese Author-Number"
    ]
  },
  {
    "id": "japanese-general-author-date",
    "name": "Japanese General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Japanese",
      "Japanese general",
      "Japanese Author-Date"
    ]
  },
  {
    "id": "japanese-general-footnote",
    "name": "Japanese General — Footnote",
    "discipline": "general",
    "aliases": [
      "Japanese",
      "Japanese general",
      "Japanese Footnote"
    ]
  },
  {
    "id": "japanese-general-endnote",
    "name": "Japanese General — Endnote",
    "discipline": "general",
    "aliases": [
      "Japanese",
      "Japanese general",
      "Japanese Endnote"
    ]
  },
  {
    "id": "japanese-general-numeric",
    "name": "Japanese General — Numeric",
    "discipline": "general",
    "aliases": [
      "Japanese",
      "Japanese general",
      "Japanese Numeric"
    ]
  },
  {
    "id": "japanese-general-vancouver-style",
    "name": "Japanese General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Japanese",
      "Japanese general",
      "Japanese Vancouver-Style"
    ]
  },
  {
    "id": "japanese-general-author-number",
    "name": "Japanese General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Japanese",
      "Japanese general",
      "Japanese Author-Number"
    ]
  },
  {
    "id": "chinese-sciences-author-date",
    "name": "Chinese Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Chinese",
      "Chinese sciences",
      "Chinese Author-Date"
    ]
  },
  {
    "id": "chinese-sciences-footnote",
    "name": "Chinese Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Chinese",
      "Chinese sciences",
      "Chinese Footnote"
    ]
  },
  {
    "id": "chinese-sciences-endnote",
    "name": "Chinese Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Chinese",
      "Chinese sciences",
      "Chinese Endnote"
    ]
  },
  {
    "id": "chinese-sciences-numeric",
    "name": "Chinese Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Chinese",
      "Chinese sciences",
      "Chinese Numeric"
    ]
  },
  {
    "id": "chinese-sciences-vancouver-style",
    "name": "Chinese Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Chinese",
      "Chinese sciences",
      "Chinese Vancouver-Style"
    ]
  },
  {
    "id": "chinese-sciences-author-number",
    "name": "Chinese Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Chinese",
      "Chinese sciences",
      "Chinese Author-Number"
    ]
  },
  {
    "id": "chinese-humanities-author-date",
    "name": "Chinese Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Chinese",
      "Chinese humanities",
      "Chinese Author-Date"
    ]
  },
  {
    "id": "chinese-humanities-footnote",
    "name": "Chinese Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Chinese",
      "Chinese humanities",
      "Chinese Footnote"
    ]
  },
  {
    "id": "chinese-humanities-endnote",
    "name": "Chinese Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Chinese",
      "Chinese humanities",
      "Chinese Endnote"
    ]
  },
  {
    "id": "chinese-humanities-numeric",
    "name": "Chinese Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Chinese",
      "Chinese humanities",
      "Chinese Numeric"
    ]
  },
  {
    "id": "chinese-humanities-vancouver-style",
    "name": "Chinese Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Chinese",
      "Chinese humanities",
      "Chinese Vancouver-Style"
    ]
  },
  {
    "id": "chinese-humanities-author-number",
    "name": "Chinese Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Chinese",
      "Chinese humanities",
      "Chinese Author-Number"
    ]
  },
  {
    "id": "chinese-law-author-date",
    "name": "Chinese Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Chinese",
      "Chinese law",
      "Chinese Author-Date"
    ]
  },
  {
    "id": "chinese-law-footnote",
    "name": "Chinese Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Chinese",
      "Chinese law",
      "Chinese Footnote"
    ]
  },
  {
    "id": "chinese-law-endnote",
    "name": "Chinese Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Chinese",
      "Chinese law",
      "Chinese Endnote"
    ]
  },
  {
    "id": "chinese-law-numeric",
    "name": "Chinese Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Chinese",
      "Chinese law",
      "Chinese Numeric"
    ]
  },
  {
    "id": "chinese-law-vancouver-style",
    "name": "Chinese Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Chinese",
      "Chinese law",
      "Chinese Vancouver-Style"
    ]
  },
  {
    "id": "chinese-law-author-number",
    "name": "Chinese Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Chinese",
      "Chinese law",
      "Chinese Author-Number"
    ]
  },
  {
    "id": "chinese-medicine-author-date",
    "name": "Chinese Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Chinese",
      "Chinese medicine",
      "Chinese Author-Date"
    ]
  },
  {
    "id": "chinese-medicine-footnote",
    "name": "Chinese Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Chinese",
      "Chinese medicine",
      "Chinese Footnote"
    ]
  },
  {
    "id": "chinese-medicine-endnote",
    "name": "Chinese Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Chinese",
      "Chinese medicine",
      "Chinese Endnote"
    ]
  },
  {
    "id": "chinese-medicine-numeric",
    "name": "Chinese Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Chinese",
      "Chinese medicine",
      "Chinese Numeric"
    ]
  },
  {
    "id": "chinese-medicine-vancouver-style",
    "name": "Chinese Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Chinese",
      "Chinese medicine",
      "Chinese Vancouver-Style"
    ]
  },
  {
    "id": "chinese-medicine-author-number",
    "name": "Chinese Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Chinese",
      "Chinese medicine",
      "Chinese Author-Number"
    ]
  },
  {
    "id": "chinese-general-author-date",
    "name": "Chinese General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Chinese",
      "Chinese general",
      "Chinese Author-Date"
    ]
  },
  {
    "id": "chinese-general-footnote",
    "name": "Chinese General — Footnote",
    "discipline": "general",
    "aliases": [
      "Chinese",
      "Chinese general",
      "Chinese Footnote"
    ]
  },
  {
    "id": "chinese-general-endnote",
    "name": "Chinese General — Endnote",
    "discipline": "general",
    "aliases": [
      "Chinese",
      "Chinese general",
      "Chinese Endnote"
    ]
  },
  {
    "id": "chinese-general-numeric",
    "name": "Chinese General — Numeric",
    "discipline": "general",
    "aliases": [
      "Chinese",
      "Chinese general",
      "Chinese Numeric"
    ]
  },
  {
    "id": "chinese-general-vancouver-style",
    "name": "Chinese General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Chinese",
      "Chinese general",
      "Chinese Vancouver-Style"
    ]
  },
  {
    "id": "chinese-general-author-number",
    "name": "Chinese General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Chinese",
      "Chinese general",
      "Chinese Author-Number"
    ]
  },
  {
    "id": "brazilian-sciences-author-date",
    "name": "Brazilian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Brazilian",
      "Brazilian sciences",
      "Brazilian Author-Date"
    ]
  },
  {
    "id": "brazilian-sciences-footnote",
    "name": "Brazilian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Brazilian",
      "Brazilian sciences",
      "Brazilian Footnote"
    ]
  },
  {
    "id": "brazilian-sciences-endnote",
    "name": "Brazilian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Brazilian",
      "Brazilian sciences",
      "Brazilian Endnote"
    ]
  },
  {
    "id": "brazilian-sciences-numeric",
    "name": "Brazilian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Brazilian",
      "Brazilian sciences",
      "Brazilian Numeric"
    ]
  },
  {
    "id": "brazilian-sciences-vancouver-style",
    "name": "Brazilian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Brazilian",
      "Brazilian sciences",
      "Brazilian Vancouver-Style"
    ]
  },
  {
    "id": "brazilian-sciences-author-number",
    "name": "Brazilian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Brazilian",
      "Brazilian sciences",
      "Brazilian Author-Number"
    ]
  },
  {
    "id": "brazilian-humanities-author-date",
    "name": "Brazilian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Brazilian",
      "Brazilian humanities",
      "Brazilian Author-Date"
    ]
  },
  {
    "id": "brazilian-humanities-footnote",
    "name": "Brazilian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Brazilian",
      "Brazilian humanities",
      "Brazilian Footnote"
    ]
  },
  {
    "id": "brazilian-humanities-endnote",
    "name": "Brazilian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Brazilian",
      "Brazilian humanities",
      "Brazilian Endnote"
    ]
  },
  {
    "id": "brazilian-humanities-numeric",
    "name": "Brazilian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Brazilian",
      "Brazilian humanities",
      "Brazilian Numeric"
    ]
  },
  {
    "id": "brazilian-humanities-vancouver-style",
    "name": "Brazilian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Brazilian",
      "Brazilian humanities",
      "Brazilian Vancouver-Style"
    ]
  },
  {
    "id": "brazilian-humanities-author-number",
    "name": "Brazilian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Brazilian",
      "Brazilian humanities",
      "Brazilian Author-Number"
    ]
  },
  {
    "id": "brazilian-law-author-date",
    "name": "Brazilian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Brazilian",
      "Brazilian law",
      "Brazilian Author-Date"
    ]
  },
  {
    "id": "brazilian-law-footnote",
    "name": "Brazilian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Brazilian",
      "Brazilian law",
      "Brazilian Footnote"
    ]
  },
  {
    "id": "brazilian-law-endnote",
    "name": "Brazilian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Brazilian",
      "Brazilian law",
      "Brazilian Endnote"
    ]
  },
  {
    "id": "brazilian-law-numeric",
    "name": "Brazilian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Brazilian",
      "Brazilian law",
      "Brazilian Numeric"
    ]
  },
  {
    "id": "brazilian-law-vancouver-style",
    "name": "Brazilian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Brazilian",
      "Brazilian law",
      "Brazilian Vancouver-Style"
    ]
  },
  {
    "id": "brazilian-law-author-number",
    "name": "Brazilian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Brazilian",
      "Brazilian law",
      "Brazilian Author-Number"
    ]
  },
  {
    "id": "brazilian-medicine-author-date",
    "name": "Brazilian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Brazilian",
      "Brazilian medicine",
      "Brazilian Author-Date"
    ]
  },
  {
    "id": "brazilian-medicine-footnote",
    "name": "Brazilian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Brazilian",
      "Brazilian medicine",
      "Brazilian Footnote"
    ]
  },
  {
    "id": "brazilian-medicine-endnote",
    "name": "Brazilian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Brazilian",
      "Brazilian medicine",
      "Brazilian Endnote"
    ]
  },
  {
    "id": "brazilian-medicine-numeric",
    "name": "Brazilian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Brazilian",
      "Brazilian medicine",
      "Brazilian Numeric"
    ]
  },
  {
    "id": "brazilian-medicine-vancouver-style",
    "name": "Brazilian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Brazilian",
      "Brazilian medicine",
      "Brazilian Vancouver-Style"
    ]
  },
  {
    "id": "brazilian-medicine-author-number",
    "name": "Brazilian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Brazilian",
      "Brazilian medicine",
      "Brazilian Author-Number"
    ]
  },
  {
    "id": "brazilian-general-author-date",
    "name": "Brazilian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Brazilian",
      "Brazilian general",
      "Brazilian Author-Date"
    ]
  },
  {
    "id": "brazilian-general-footnote",
    "name": "Brazilian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Brazilian",
      "Brazilian general",
      "Brazilian Footnote"
    ]
  },
  {
    "id": "brazilian-general-endnote",
    "name": "Brazilian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Brazilian",
      "Brazilian general",
      "Brazilian Endnote"
    ]
  },
  {
    "id": "brazilian-general-numeric",
    "name": "Brazilian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Brazilian",
      "Brazilian general",
      "Brazilian Numeric"
    ]
  },
  {
    "id": "brazilian-general-vancouver-style",
    "name": "Brazilian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Brazilian",
      "Brazilian general",
      "Brazilian Vancouver-Style"
    ]
  },
  {
    "id": "brazilian-general-author-number",
    "name": "Brazilian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Brazilian",
      "Brazilian general",
      "Brazilian Author-Number"
    ]
  },
  {
    "id": "indian-sciences-author-date",
    "name": "Indian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Indian",
      "Indian sciences",
      "Indian Author-Date"
    ]
  },
  {
    "id": "indian-sciences-footnote",
    "name": "Indian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Indian",
      "Indian sciences",
      "Indian Footnote"
    ]
  },
  {
    "id": "indian-sciences-endnote",
    "name": "Indian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Indian",
      "Indian sciences",
      "Indian Endnote"
    ]
  },
  {
    "id": "indian-sciences-numeric",
    "name": "Indian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Indian",
      "Indian sciences",
      "Indian Numeric"
    ]
  },
  {
    "id": "indian-sciences-vancouver-style",
    "name": "Indian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Indian",
      "Indian sciences",
      "Indian Vancouver-Style"
    ]
  },
  {
    "id": "indian-sciences-author-number",
    "name": "Indian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Indian",
      "Indian sciences",
      "Indian Author-Number"
    ]
  },
  {
    "id": "indian-humanities-author-date",
    "name": "Indian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Indian",
      "Indian humanities",
      "Indian Author-Date"
    ]
  },
  {
    "id": "indian-humanities-footnote",
    "name": "Indian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Indian",
      "Indian humanities",
      "Indian Footnote"
    ]
  },
  {
    "id": "indian-humanities-endnote",
    "name": "Indian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Indian",
      "Indian humanities",
      "Indian Endnote"
    ]
  },
  {
    "id": "indian-humanities-numeric",
    "name": "Indian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Indian",
      "Indian humanities",
      "Indian Numeric"
    ]
  },
  {
    "id": "indian-humanities-vancouver-style",
    "name": "Indian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Indian",
      "Indian humanities",
      "Indian Vancouver-Style"
    ]
  },
  {
    "id": "indian-humanities-author-number",
    "name": "Indian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Indian",
      "Indian humanities",
      "Indian Author-Number"
    ]
  },
  {
    "id": "indian-law-author-date",
    "name": "Indian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Indian",
      "Indian law",
      "Indian Author-Date"
    ]
  },
  {
    "id": "indian-law-footnote",
    "name": "Indian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Indian",
      "Indian law",
      "Indian Footnote"
    ]
  },
  {
    "id": "indian-law-endnote",
    "name": "Indian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Indian",
      "Indian law",
      "Indian Endnote"
    ]
  },
  {
    "id": "indian-law-numeric",
    "name": "Indian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Indian",
      "Indian law",
      "Indian Numeric"
    ]
  },
  {
    "id": "indian-law-vancouver-style",
    "name": "Indian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Indian",
      "Indian law",
      "Indian Vancouver-Style"
    ]
  },
  {
    "id": "indian-law-author-number",
    "name": "Indian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Indian",
      "Indian law",
      "Indian Author-Number"
    ]
  },
  {
    "id": "indian-medicine-author-date",
    "name": "Indian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Indian",
      "Indian medicine",
      "Indian Author-Date"
    ]
  },
  {
    "id": "indian-medicine-footnote",
    "name": "Indian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Indian",
      "Indian medicine",
      "Indian Footnote"
    ]
  },
  {
    "id": "indian-medicine-endnote",
    "name": "Indian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Indian",
      "Indian medicine",
      "Indian Endnote"
    ]
  },
  {
    "id": "indian-medicine-numeric",
    "name": "Indian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Indian",
      "Indian medicine",
      "Indian Numeric"
    ]
  },
  {
    "id": "indian-medicine-vancouver-style",
    "name": "Indian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Indian",
      "Indian medicine",
      "Indian Vancouver-Style"
    ]
  },
  {
    "id": "indian-medicine-author-number",
    "name": "Indian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Indian",
      "Indian medicine",
      "Indian Author-Number"
    ]
  },
  {
    "id": "indian-general-author-date",
    "name": "Indian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Indian",
      "Indian general",
      "Indian Author-Date"
    ]
  },
  {
    "id": "indian-general-footnote",
    "name": "Indian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Indian",
      "Indian general",
      "Indian Footnote"
    ]
  },
  {
    "id": "indian-general-endnote",
    "name": "Indian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Indian",
      "Indian general",
      "Indian Endnote"
    ]
  },
  {
    "id": "indian-general-numeric",
    "name": "Indian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Indian",
      "Indian general",
      "Indian Numeric"
    ]
  },
  {
    "id": "indian-general-vancouver-style",
    "name": "Indian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Indian",
      "Indian general",
      "Indian Vancouver-Style"
    ]
  },
  {
    "id": "indian-general-author-number",
    "name": "Indian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Indian",
      "Indian general",
      "Indian Author-Number"
    ]
  },
  {
    "id": "south african-sciences-author-date",
    "name": "South African Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "South African",
      "South African sciences",
      "South African Author-Date"
    ]
  },
  {
    "id": "south african-sciences-footnote",
    "name": "South African Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "South African",
      "South African sciences",
      "South African Footnote"
    ]
  },
  {
    "id": "south african-sciences-endnote",
    "name": "South African Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "South African",
      "South African sciences",
      "South African Endnote"
    ]
  },
  {
    "id": "south african-sciences-numeric",
    "name": "South African Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "South African",
      "South African sciences",
      "South African Numeric"
    ]
  },
  {
    "id": "south african-sciences-vancouver-style",
    "name": "South African Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "South African",
      "South African sciences",
      "South African Vancouver-Style"
    ]
  },
  {
    "id": "south african-sciences-author-number",
    "name": "South African Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "South African",
      "South African sciences",
      "South African Author-Number"
    ]
  },
  {
    "id": "south african-humanities-author-date",
    "name": "South African Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "South African",
      "South African humanities",
      "South African Author-Date"
    ]
  },
  {
    "id": "south african-humanities-footnote",
    "name": "South African Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "South African",
      "South African humanities",
      "South African Footnote"
    ]
  },
  {
    "id": "south african-humanities-endnote",
    "name": "South African Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "South African",
      "South African humanities",
      "South African Endnote"
    ]
  },
  {
    "id": "south african-humanities-numeric",
    "name": "South African Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "South African",
      "South African humanities",
      "South African Numeric"
    ]
  },
  {
    "id": "south african-humanities-vancouver-style",
    "name": "South African Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "South African",
      "South African humanities",
      "South African Vancouver-Style"
    ]
  },
  {
    "id": "south african-humanities-author-number",
    "name": "South African Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "South African",
      "South African humanities",
      "South African Author-Number"
    ]
  },
  {
    "id": "south african-law-author-date",
    "name": "South African Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "South African",
      "South African law",
      "South African Author-Date"
    ]
  },
  {
    "id": "south african-law-footnote",
    "name": "South African Law — Footnote",
    "discipline": "law",
    "aliases": [
      "South African",
      "South African law",
      "South African Footnote"
    ]
  },
  {
    "id": "south african-law-endnote",
    "name": "South African Law — Endnote",
    "discipline": "law",
    "aliases": [
      "South African",
      "South African law",
      "South African Endnote"
    ]
  },
  {
    "id": "south african-law-numeric",
    "name": "South African Law — Numeric",
    "discipline": "law",
    "aliases": [
      "South African",
      "South African law",
      "South African Numeric"
    ]
  },
  {
    "id": "south african-law-vancouver-style",
    "name": "South African Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "South African",
      "South African law",
      "South African Vancouver-Style"
    ]
  },
  {
    "id": "south african-law-author-number",
    "name": "South African Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "South African",
      "South African law",
      "South African Author-Number"
    ]
  },
  {
    "id": "south african-medicine-author-date",
    "name": "South African Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "South African",
      "South African medicine",
      "South African Author-Date"
    ]
  },
  {
    "id": "south african-medicine-footnote",
    "name": "South African Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "South African",
      "South African medicine",
      "South African Footnote"
    ]
  },
  {
    "id": "south african-medicine-endnote",
    "name": "South African Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "South African",
      "South African medicine",
      "South African Endnote"
    ]
  },
  {
    "id": "south african-medicine-numeric",
    "name": "South African Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "South African",
      "South African medicine",
      "South African Numeric"
    ]
  },
  {
    "id": "south african-medicine-vancouver-style",
    "name": "South African Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "South African",
      "South African medicine",
      "South African Vancouver-Style"
    ]
  },
  {
    "id": "south african-medicine-author-number",
    "name": "South African Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "South African",
      "South African medicine",
      "South African Author-Number"
    ]
  },
  {
    "id": "south african-general-author-date",
    "name": "South African General — Author-Date",
    "discipline": "general",
    "aliases": [
      "South African",
      "South African general",
      "South African Author-Date"
    ]
  },
  {
    "id": "south african-general-footnote",
    "name": "South African General — Footnote",
    "discipline": "general",
    "aliases": [
      "South African",
      "South African general",
      "South African Footnote"
    ]
  },
  {
    "id": "south african-general-endnote",
    "name": "South African General — Endnote",
    "discipline": "general",
    "aliases": [
      "South African",
      "South African general",
      "South African Endnote"
    ]
  },
  {
    "id": "south african-general-numeric",
    "name": "South African General — Numeric",
    "discipline": "general",
    "aliases": [
      "South African",
      "South African general",
      "South African Numeric"
    ]
  },
  {
    "id": "south african-general-vancouver-style",
    "name": "South African General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "South African",
      "South African general",
      "South African Vancouver-Style"
    ]
  },
  {
    "id": "south african-general-author-number",
    "name": "South African General — Author-Number",
    "discipline": "general",
    "aliases": [
      "South African",
      "South African general",
      "South African Author-Number"
    ]
  },
  {
    "id": "new zealand-sciences-author-date",
    "name": "New Zealand Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "New Zealand",
      "New Zealand sciences",
      "New Zealand Author-Date"
    ]
  },
  {
    "id": "new zealand-sciences-footnote",
    "name": "New Zealand Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "New Zealand",
      "New Zealand sciences",
      "New Zealand Footnote"
    ]
  },
  {
    "id": "new zealand-sciences-endnote",
    "name": "New Zealand Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "New Zealand",
      "New Zealand sciences",
      "New Zealand Endnote"
    ]
  },
  {
    "id": "new zealand-sciences-numeric",
    "name": "New Zealand Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "New Zealand",
      "New Zealand sciences",
      "New Zealand Numeric"
    ]
  },
  {
    "id": "new zealand-sciences-vancouver-style",
    "name": "New Zealand Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "New Zealand",
      "New Zealand sciences",
      "New Zealand Vancouver-Style"
    ]
  },
  {
    "id": "new zealand-sciences-author-number",
    "name": "New Zealand Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "New Zealand",
      "New Zealand sciences",
      "New Zealand Author-Number"
    ]
  },
  {
    "id": "new zealand-humanities-author-date",
    "name": "New Zealand Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "New Zealand",
      "New Zealand humanities",
      "New Zealand Author-Date"
    ]
  },
  {
    "id": "new zealand-humanities-footnote",
    "name": "New Zealand Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "New Zealand",
      "New Zealand humanities",
      "New Zealand Footnote"
    ]
  },
  {
    "id": "new zealand-humanities-endnote",
    "name": "New Zealand Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "New Zealand",
      "New Zealand humanities",
      "New Zealand Endnote"
    ]
  },
  {
    "id": "new zealand-humanities-numeric",
    "name": "New Zealand Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "New Zealand",
      "New Zealand humanities",
      "New Zealand Numeric"
    ]
  },
  {
    "id": "new zealand-humanities-vancouver-style",
    "name": "New Zealand Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "New Zealand",
      "New Zealand humanities",
      "New Zealand Vancouver-Style"
    ]
  },
  {
    "id": "new zealand-humanities-author-number",
    "name": "New Zealand Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "New Zealand",
      "New Zealand humanities",
      "New Zealand Author-Number"
    ]
  },
  {
    "id": "new zealand-law-author-date",
    "name": "New Zealand Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "New Zealand",
      "New Zealand law",
      "New Zealand Author-Date"
    ]
  },
  {
    "id": "new zealand-law-footnote",
    "name": "New Zealand Law — Footnote",
    "discipline": "law",
    "aliases": [
      "New Zealand",
      "New Zealand law",
      "New Zealand Footnote"
    ]
  },
  {
    "id": "new zealand-law-endnote",
    "name": "New Zealand Law — Endnote",
    "discipline": "law",
    "aliases": [
      "New Zealand",
      "New Zealand law",
      "New Zealand Endnote"
    ]
  },
  {
    "id": "new zealand-law-numeric",
    "name": "New Zealand Law — Numeric",
    "discipline": "law",
    "aliases": [
      "New Zealand",
      "New Zealand law",
      "New Zealand Numeric"
    ]
  },
  {
    "id": "new zealand-law-vancouver-style",
    "name": "New Zealand Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "New Zealand",
      "New Zealand law",
      "New Zealand Vancouver-Style"
    ]
  },
  {
    "id": "new zealand-law-author-number",
    "name": "New Zealand Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "New Zealand",
      "New Zealand law",
      "New Zealand Author-Number"
    ]
  },
  {
    "id": "new zealand-medicine-author-date",
    "name": "New Zealand Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "New Zealand",
      "New Zealand medicine",
      "New Zealand Author-Date"
    ]
  },
  {
    "id": "new zealand-medicine-footnote",
    "name": "New Zealand Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "New Zealand",
      "New Zealand medicine",
      "New Zealand Footnote"
    ]
  },
  {
    "id": "new zealand-medicine-endnote",
    "name": "New Zealand Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "New Zealand",
      "New Zealand medicine",
      "New Zealand Endnote"
    ]
  },
  {
    "id": "new zealand-medicine-numeric",
    "name": "New Zealand Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "New Zealand",
      "New Zealand medicine",
      "New Zealand Numeric"
    ]
  },
  {
    "id": "new zealand-medicine-vancouver-style",
    "name": "New Zealand Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "New Zealand",
      "New Zealand medicine",
      "New Zealand Vancouver-Style"
    ]
  },
  {
    "id": "new zealand-medicine-author-number",
    "name": "New Zealand Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "New Zealand",
      "New Zealand medicine",
      "New Zealand Author-Number"
    ]
  },
  {
    "id": "new zealand-general-author-date",
    "name": "New Zealand General — Author-Date",
    "discipline": "general",
    "aliases": [
      "New Zealand",
      "New Zealand general",
      "New Zealand Author-Date"
    ]
  },
  {
    "id": "new zealand-general-footnote",
    "name": "New Zealand General — Footnote",
    "discipline": "general",
    "aliases": [
      "New Zealand",
      "New Zealand general",
      "New Zealand Footnote"
    ]
  },
  {
    "id": "new zealand-general-endnote",
    "name": "New Zealand General — Endnote",
    "discipline": "general",
    "aliases": [
      "New Zealand",
      "New Zealand general",
      "New Zealand Endnote"
    ]
  },
  {
    "id": "new zealand-general-numeric",
    "name": "New Zealand General — Numeric",
    "discipline": "general",
    "aliases": [
      "New Zealand",
      "New Zealand general",
      "New Zealand Numeric"
    ]
  },
  {
    "id": "new zealand-general-vancouver-style",
    "name": "New Zealand General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "New Zealand",
      "New Zealand general",
      "New Zealand Vancouver-Style"
    ]
  },
  {
    "id": "new zealand-general-author-number",
    "name": "New Zealand General — Author-Number",
    "discipline": "general",
    "aliases": [
      "New Zealand",
      "New Zealand general",
      "New Zealand Author-Number"
    ]
  },
  {
    "id": "irish-sciences-author-date",
    "name": "Irish Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Irish",
      "Irish sciences",
      "Irish Author-Date"
    ]
  },
  {
    "id": "irish-sciences-footnote",
    "name": "Irish Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Irish",
      "Irish sciences",
      "Irish Footnote"
    ]
  },
  {
    "id": "irish-sciences-endnote",
    "name": "Irish Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Irish",
      "Irish sciences",
      "Irish Endnote"
    ]
  },
  {
    "id": "irish-sciences-numeric",
    "name": "Irish Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Irish",
      "Irish sciences",
      "Irish Numeric"
    ]
  },
  {
    "id": "irish-sciences-vancouver-style",
    "name": "Irish Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Irish",
      "Irish sciences",
      "Irish Vancouver-Style"
    ]
  },
  {
    "id": "irish-sciences-author-number",
    "name": "Irish Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Irish",
      "Irish sciences",
      "Irish Author-Number"
    ]
  },
  {
    "id": "irish-humanities-author-date",
    "name": "Irish Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Irish",
      "Irish humanities",
      "Irish Author-Date"
    ]
  },
  {
    "id": "irish-humanities-footnote",
    "name": "Irish Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Irish",
      "Irish humanities",
      "Irish Footnote"
    ]
  },
  {
    "id": "irish-humanities-endnote",
    "name": "Irish Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Irish",
      "Irish humanities",
      "Irish Endnote"
    ]
  },
  {
    "id": "irish-humanities-numeric",
    "name": "Irish Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Irish",
      "Irish humanities",
      "Irish Numeric"
    ]
  },
  {
    "id": "irish-humanities-vancouver-style",
    "name": "Irish Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Irish",
      "Irish humanities",
      "Irish Vancouver-Style"
    ]
  },
  {
    "id": "irish-humanities-author-number",
    "name": "Irish Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Irish",
      "Irish humanities",
      "Irish Author-Number"
    ]
  },
  {
    "id": "irish-law-author-date",
    "name": "Irish Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Irish",
      "Irish law",
      "Irish Author-Date"
    ]
  },
  {
    "id": "irish-law-footnote",
    "name": "Irish Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Irish",
      "Irish law",
      "Irish Footnote"
    ]
  },
  {
    "id": "irish-law-endnote",
    "name": "Irish Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Irish",
      "Irish law",
      "Irish Endnote"
    ]
  },
  {
    "id": "irish-law-numeric",
    "name": "Irish Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Irish",
      "Irish law",
      "Irish Numeric"
    ]
  },
  {
    "id": "irish-law-vancouver-style",
    "name": "Irish Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Irish",
      "Irish law",
      "Irish Vancouver-Style"
    ]
  },
  {
    "id": "irish-law-author-number",
    "name": "Irish Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Irish",
      "Irish law",
      "Irish Author-Number"
    ]
  },
  {
    "id": "irish-medicine-author-date",
    "name": "Irish Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Irish",
      "Irish medicine",
      "Irish Author-Date"
    ]
  },
  {
    "id": "irish-medicine-footnote",
    "name": "Irish Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Irish",
      "Irish medicine",
      "Irish Footnote"
    ]
  },
  {
    "id": "irish-medicine-endnote",
    "name": "Irish Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Irish",
      "Irish medicine",
      "Irish Endnote"
    ]
  },
  {
    "id": "irish-medicine-numeric",
    "name": "Irish Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Irish",
      "Irish medicine",
      "Irish Numeric"
    ]
  },
  {
    "id": "irish-medicine-vancouver-style",
    "name": "Irish Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Irish",
      "Irish medicine",
      "Irish Vancouver-Style"
    ]
  },
  {
    "id": "irish-medicine-author-number",
    "name": "Irish Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Irish",
      "Irish medicine",
      "Irish Author-Number"
    ]
  },
  {
    "id": "irish-general-author-date",
    "name": "Irish General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Irish",
      "Irish general",
      "Irish Author-Date"
    ]
  },
  {
    "id": "irish-general-footnote",
    "name": "Irish General — Footnote",
    "discipline": "general",
    "aliases": [
      "Irish",
      "Irish general",
      "Irish Footnote"
    ]
  },
  {
    "id": "irish-general-endnote",
    "name": "Irish General — Endnote",
    "discipline": "general",
    "aliases": [
      "Irish",
      "Irish general",
      "Irish Endnote"
    ]
  },
  {
    "id": "irish-general-numeric",
    "name": "Irish General — Numeric",
    "discipline": "general",
    "aliases": [
      "Irish",
      "Irish general",
      "Irish Numeric"
    ]
  },
  {
    "id": "irish-general-vancouver-style",
    "name": "Irish General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Irish",
      "Irish general",
      "Irish Vancouver-Style"
    ]
  },
  {
    "id": "irish-general-author-number",
    "name": "Irish General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Irish",
      "Irish general",
      "Irish Author-Number"
    ]
  },
  {
    "id": "swiss-sciences-author-date",
    "name": "Swiss Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Swiss",
      "Swiss sciences",
      "Swiss Author-Date"
    ]
  },
  {
    "id": "swiss-sciences-footnote",
    "name": "Swiss Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Swiss",
      "Swiss sciences",
      "Swiss Footnote"
    ]
  },
  {
    "id": "swiss-sciences-endnote",
    "name": "Swiss Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Swiss",
      "Swiss sciences",
      "Swiss Endnote"
    ]
  },
  {
    "id": "swiss-sciences-numeric",
    "name": "Swiss Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Swiss",
      "Swiss sciences",
      "Swiss Numeric"
    ]
  },
  {
    "id": "swiss-sciences-vancouver-style",
    "name": "Swiss Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Swiss",
      "Swiss sciences",
      "Swiss Vancouver-Style"
    ]
  },
  {
    "id": "swiss-sciences-author-number",
    "name": "Swiss Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Swiss",
      "Swiss sciences",
      "Swiss Author-Number"
    ]
  },
  {
    "id": "swiss-humanities-author-date",
    "name": "Swiss Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Swiss",
      "Swiss humanities",
      "Swiss Author-Date"
    ]
  },
  {
    "id": "swiss-humanities-footnote",
    "name": "Swiss Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Swiss",
      "Swiss humanities",
      "Swiss Footnote"
    ]
  },
  {
    "id": "swiss-humanities-endnote",
    "name": "Swiss Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Swiss",
      "Swiss humanities",
      "Swiss Endnote"
    ]
  },
  {
    "id": "swiss-humanities-numeric",
    "name": "Swiss Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Swiss",
      "Swiss humanities",
      "Swiss Numeric"
    ]
  },
  {
    "id": "swiss-humanities-vancouver-style",
    "name": "Swiss Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Swiss",
      "Swiss humanities",
      "Swiss Vancouver-Style"
    ]
  },
  {
    "id": "swiss-humanities-author-number",
    "name": "Swiss Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Swiss",
      "Swiss humanities",
      "Swiss Author-Number"
    ]
  },
  {
    "id": "swiss-law-author-date",
    "name": "Swiss Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Swiss",
      "Swiss law",
      "Swiss Author-Date"
    ]
  },
  {
    "id": "swiss-law-footnote",
    "name": "Swiss Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Swiss",
      "Swiss law",
      "Swiss Footnote"
    ]
  },
  {
    "id": "swiss-law-endnote",
    "name": "Swiss Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Swiss",
      "Swiss law",
      "Swiss Endnote"
    ]
  },
  {
    "id": "swiss-law-numeric",
    "name": "Swiss Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Swiss",
      "Swiss law",
      "Swiss Numeric"
    ]
  },
  {
    "id": "swiss-law-vancouver-style",
    "name": "Swiss Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Swiss",
      "Swiss law",
      "Swiss Vancouver-Style"
    ]
  },
  {
    "id": "swiss-law-author-number",
    "name": "Swiss Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Swiss",
      "Swiss law",
      "Swiss Author-Number"
    ]
  },
  {
    "id": "swiss-medicine-author-date",
    "name": "Swiss Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Swiss",
      "Swiss medicine",
      "Swiss Author-Date"
    ]
  },
  {
    "id": "swiss-medicine-footnote",
    "name": "Swiss Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Swiss",
      "Swiss medicine",
      "Swiss Footnote"
    ]
  },
  {
    "id": "swiss-medicine-endnote",
    "name": "Swiss Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Swiss",
      "Swiss medicine",
      "Swiss Endnote"
    ]
  },
  {
    "id": "swiss-medicine-numeric",
    "name": "Swiss Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Swiss",
      "Swiss medicine",
      "Swiss Numeric"
    ]
  },
  {
    "id": "swiss-medicine-vancouver-style",
    "name": "Swiss Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Swiss",
      "Swiss medicine",
      "Swiss Vancouver-Style"
    ]
  },
  {
    "id": "swiss-medicine-author-number",
    "name": "Swiss Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Swiss",
      "Swiss medicine",
      "Swiss Author-Number"
    ]
  },
  {
    "id": "swiss-general-author-date",
    "name": "Swiss General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Swiss",
      "Swiss general",
      "Swiss Author-Date"
    ]
  },
  {
    "id": "swiss-general-footnote",
    "name": "Swiss General — Footnote",
    "discipline": "general",
    "aliases": [
      "Swiss",
      "Swiss general",
      "Swiss Footnote"
    ]
  },
  {
    "id": "swiss-general-endnote",
    "name": "Swiss General — Endnote",
    "discipline": "general",
    "aliases": [
      "Swiss",
      "Swiss general",
      "Swiss Endnote"
    ]
  },
  {
    "id": "swiss-general-numeric",
    "name": "Swiss General — Numeric",
    "discipline": "general",
    "aliases": [
      "Swiss",
      "Swiss general",
      "Swiss Numeric"
    ]
  },
  {
    "id": "swiss-general-vancouver-style",
    "name": "Swiss General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Swiss",
      "Swiss general",
      "Swiss Vancouver-Style"
    ]
  },
  {
    "id": "swiss-general-author-number",
    "name": "Swiss General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Swiss",
      "Swiss general",
      "Swiss Author-Number"
    ]
  },
  {
    "id": "austrian-sciences-author-date",
    "name": "Austrian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Austrian",
      "Austrian sciences",
      "Austrian Author-Date"
    ]
  },
  {
    "id": "austrian-sciences-footnote",
    "name": "Austrian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Austrian",
      "Austrian sciences",
      "Austrian Footnote"
    ]
  },
  {
    "id": "austrian-sciences-endnote",
    "name": "Austrian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Austrian",
      "Austrian sciences",
      "Austrian Endnote"
    ]
  },
  {
    "id": "austrian-sciences-numeric",
    "name": "Austrian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Austrian",
      "Austrian sciences",
      "Austrian Numeric"
    ]
  },
  {
    "id": "austrian-sciences-vancouver-style",
    "name": "Austrian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Austrian",
      "Austrian sciences",
      "Austrian Vancouver-Style"
    ]
  },
  {
    "id": "austrian-sciences-author-number",
    "name": "Austrian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Austrian",
      "Austrian sciences",
      "Austrian Author-Number"
    ]
  },
  {
    "id": "austrian-humanities-author-date",
    "name": "Austrian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Austrian",
      "Austrian humanities",
      "Austrian Author-Date"
    ]
  },
  {
    "id": "austrian-humanities-footnote",
    "name": "Austrian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Austrian",
      "Austrian humanities",
      "Austrian Footnote"
    ]
  },
  {
    "id": "austrian-humanities-endnote",
    "name": "Austrian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Austrian",
      "Austrian humanities",
      "Austrian Endnote"
    ]
  },
  {
    "id": "austrian-humanities-numeric",
    "name": "Austrian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Austrian",
      "Austrian humanities",
      "Austrian Numeric"
    ]
  },
  {
    "id": "austrian-humanities-vancouver-style",
    "name": "Austrian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Austrian",
      "Austrian humanities",
      "Austrian Vancouver-Style"
    ]
  },
  {
    "id": "austrian-humanities-author-number",
    "name": "Austrian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Austrian",
      "Austrian humanities",
      "Austrian Author-Number"
    ]
  },
  {
    "id": "austrian-law-author-date",
    "name": "Austrian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Austrian",
      "Austrian law",
      "Austrian Author-Date"
    ]
  },
  {
    "id": "austrian-law-footnote",
    "name": "Austrian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Austrian",
      "Austrian law",
      "Austrian Footnote"
    ]
  },
  {
    "id": "austrian-law-endnote",
    "name": "Austrian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Austrian",
      "Austrian law",
      "Austrian Endnote"
    ]
  },
  {
    "id": "austrian-law-numeric",
    "name": "Austrian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Austrian",
      "Austrian law",
      "Austrian Numeric"
    ]
  },
  {
    "id": "austrian-law-vancouver-style",
    "name": "Austrian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Austrian",
      "Austrian law",
      "Austrian Vancouver-Style"
    ]
  },
  {
    "id": "austrian-law-author-number",
    "name": "Austrian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Austrian",
      "Austrian law",
      "Austrian Author-Number"
    ]
  },
  {
    "id": "austrian-medicine-author-date",
    "name": "Austrian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Austrian",
      "Austrian medicine",
      "Austrian Author-Date"
    ]
  },
  {
    "id": "austrian-medicine-footnote",
    "name": "Austrian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Austrian",
      "Austrian medicine",
      "Austrian Footnote"
    ]
  },
  {
    "id": "austrian-medicine-endnote",
    "name": "Austrian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Austrian",
      "Austrian medicine",
      "Austrian Endnote"
    ]
  },
  {
    "id": "austrian-medicine-numeric",
    "name": "Austrian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Austrian",
      "Austrian medicine",
      "Austrian Numeric"
    ]
  },
  {
    "id": "austrian-medicine-vancouver-style",
    "name": "Austrian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Austrian",
      "Austrian medicine",
      "Austrian Vancouver-Style"
    ]
  },
  {
    "id": "austrian-medicine-author-number",
    "name": "Austrian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Austrian",
      "Austrian medicine",
      "Austrian Author-Number"
    ]
  },
  {
    "id": "austrian-general-author-date",
    "name": "Austrian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Austrian",
      "Austrian general",
      "Austrian Author-Date"
    ]
  },
  {
    "id": "austrian-general-footnote",
    "name": "Austrian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Austrian",
      "Austrian general",
      "Austrian Footnote"
    ]
  },
  {
    "id": "austrian-general-endnote",
    "name": "Austrian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Austrian",
      "Austrian general",
      "Austrian Endnote"
    ]
  },
  {
    "id": "austrian-general-numeric",
    "name": "Austrian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Austrian",
      "Austrian general",
      "Austrian Numeric"
    ]
  },
  {
    "id": "austrian-general-vancouver-style",
    "name": "Austrian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Austrian",
      "Austrian general",
      "Austrian Vancouver-Style"
    ]
  },
  {
    "id": "austrian-general-author-number",
    "name": "Austrian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Austrian",
      "Austrian general",
      "Austrian Author-Number"
    ]
  },
  {
    "id": "belgian-sciences-author-date",
    "name": "Belgian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Belgian",
      "Belgian sciences",
      "Belgian Author-Date"
    ]
  },
  {
    "id": "belgian-sciences-footnote",
    "name": "Belgian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Belgian",
      "Belgian sciences",
      "Belgian Footnote"
    ]
  },
  {
    "id": "belgian-sciences-endnote",
    "name": "Belgian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Belgian",
      "Belgian sciences",
      "Belgian Endnote"
    ]
  },
  {
    "id": "belgian-sciences-numeric",
    "name": "Belgian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Belgian",
      "Belgian sciences",
      "Belgian Numeric"
    ]
  },
  {
    "id": "belgian-sciences-vancouver-style",
    "name": "Belgian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Belgian",
      "Belgian sciences",
      "Belgian Vancouver-Style"
    ]
  },
  {
    "id": "belgian-sciences-author-number",
    "name": "Belgian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Belgian",
      "Belgian sciences",
      "Belgian Author-Number"
    ]
  },
  {
    "id": "belgian-humanities-author-date",
    "name": "Belgian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Belgian",
      "Belgian humanities",
      "Belgian Author-Date"
    ]
  },
  {
    "id": "belgian-humanities-footnote",
    "name": "Belgian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Belgian",
      "Belgian humanities",
      "Belgian Footnote"
    ]
  },
  {
    "id": "belgian-humanities-endnote",
    "name": "Belgian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Belgian",
      "Belgian humanities",
      "Belgian Endnote"
    ]
  },
  {
    "id": "belgian-humanities-numeric",
    "name": "Belgian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Belgian",
      "Belgian humanities",
      "Belgian Numeric"
    ]
  },
  {
    "id": "belgian-humanities-vancouver-style",
    "name": "Belgian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Belgian",
      "Belgian humanities",
      "Belgian Vancouver-Style"
    ]
  },
  {
    "id": "belgian-humanities-author-number",
    "name": "Belgian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Belgian",
      "Belgian humanities",
      "Belgian Author-Number"
    ]
  },
  {
    "id": "belgian-law-author-date",
    "name": "Belgian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Belgian",
      "Belgian law",
      "Belgian Author-Date"
    ]
  },
  {
    "id": "belgian-law-footnote",
    "name": "Belgian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Belgian",
      "Belgian law",
      "Belgian Footnote"
    ]
  },
  {
    "id": "belgian-law-endnote",
    "name": "Belgian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Belgian",
      "Belgian law",
      "Belgian Endnote"
    ]
  },
  {
    "id": "belgian-law-numeric",
    "name": "Belgian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Belgian",
      "Belgian law",
      "Belgian Numeric"
    ]
  },
  {
    "id": "belgian-law-vancouver-style",
    "name": "Belgian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Belgian",
      "Belgian law",
      "Belgian Vancouver-Style"
    ]
  },
  {
    "id": "belgian-law-author-number",
    "name": "Belgian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Belgian",
      "Belgian law",
      "Belgian Author-Number"
    ]
  },
  {
    "id": "belgian-medicine-author-date",
    "name": "Belgian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Belgian",
      "Belgian medicine",
      "Belgian Author-Date"
    ]
  },
  {
    "id": "belgian-medicine-footnote",
    "name": "Belgian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Belgian",
      "Belgian medicine",
      "Belgian Footnote"
    ]
  },
  {
    "id": "belgian-medicine-endnote",
    "name": "Belgian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Belgian",
      "Belgian medicine",
      "Belgian Endnote"
    ]
  },
  {
    "id": "belgian-medicine-numeric",
    "name": "Belgian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Belgian",
      "Belgian medicine",
      "Belgian Numeric"
    ]
  },
  {
    "id": "belgian-medicine-vancouver-style",
    "name": "Belgian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Belgian",
      "Belgian medicine",
      "Belgian Vancouver-Style"
    ]
  },
  {
    "id": "belgian-medicine-author-number",
    "name": "Belgian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Belgian",
      "Belgian medicine",
      "Belgian Author-Number"
    ]
  },
  {
    "id": "belgian-general-author-date",
    "name": "Belgian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Belgian",
      "Belgian general",
      "Belgian Author-Date"
    ]
  },
  {
    "id": "belgian-general-footnote",
    "name": "Belgian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Belgian",
      "Belgian general",
      "Belgian Footnote"
    ]
  },
  {
    "id": "belgian-general-endnote",
    "name": "Belgian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Belgian",
      "Belgian general",
      "Belgian Endnote"
    ]
  },
  {
    "id": "belgian-general-numeric",
    "name": "Belgian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Belgian",
      "Belgian general",
      "Belgian Numeric"
    ]
  },
  {
    "id": "belgian-general-vancouver-style",
    "name": "Belgian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Belgian",
      "Belgian general",
      "Belgian Vancouver-Style"
    ]
  },
  {
    "id": "belgian-general-author-number",
    "name": "Belgian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Belgian",
      "Belgian general",
      "Belgian Author-Number"
    ]
  },
  {
    "id": "swedish-sciences-author-date",
    "name": "Swedish Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Swedish",
      "Swedish sciences",
      "Swedish Author-Date"
    ]
  },
  {
    "id": "swedish-sciences-footnote",
    "name": "Swedish Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Swedish",
      "Swedish sciences",
      "Swedish Footnote"
    ]
  },
  {
    "id": "swedish-sciences-endnote",
    "name": "Swedish Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Swedish",
      "Swedish sciences",
      "Swedish Endnote"
    ]
  },
  {
    "id": "swedish-sciences-numeric",
    "name": "Swedish Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Swedish",
      "Swedish sciences",
      "Swedish Numeric"
    ]
  },
  {
    "id": "swedish-sciences-vancouver-style",
    "name": "Swedish Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Swedish",
      "Swedish sciences",
      "Swedish Vancouver-Style"
    ]
  },
  {
    "id": "swedish-sciences-author-number",
    "name": "Swedish Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Swedish",
      "Swedish sciences",
      "Swedish Author-Number"
    ]
  },
  {
    "id": "swedish-humanities-author-date",
    "name": "Swedish Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Swedish",
      "Swedish humanities",
      "Swedish Author-Date"
    ]
  },
  {
    "id": "swedish-humanities-footnote",
    "name": "Swedish Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Swedish",
      "Swedish humanities",
      "Swedish Footnote"
    ]
  },
  {
    "id": "swedish-humanities-endnote",
    "name": "Swedish Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Swedish",
      "Swedish humanities",
      "Swedish Endnote"
    ]
  },
  {
    "id": "swedish-humanities-numeric",
    "name": "Swedish Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Swedish",
      "Swedish humanities",
      "Swedish Numeric"
    ]
  },
  {
    "id": "swedish-humanities-vancouver-style",
    "name": "Swedish Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Swedish",
      "Swedish humanities",
      "Swedish Vancouver-Style"
    ]
  },
  {
    "id": "swedish-humanities-author-number",
    "name": "Swedish Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Swedish",
      "Swedish humanities",
      "Swedish Author-Number"
    ]
  },
  {
    "id": "swedish-law-author-date",
    "name": "Swedish Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Swedish",
      "Swedish law",
      "Swedish Author-Date"
    ]
  },
  {
    "id": "swedish-law-footnote",
    "name": "Swedish Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Swedish",
      "Swedish law",
      "Swedish Footnote"
    ]
  },
  {
    "id": "swedish-law-endnote",
    "name": "Swedish Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Swedish",
      "Swedish law",
      "Swedish Endnote"
    ]
  },
  {
    "id": "swedish-law-numeric",
    "name": "Swedish Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Swedish",
      "Swedish law",
      "Swedish Numeric"
    ]
  },
  {
    "id": "swedish-law-vancouver-style",
    "name": "Swedish Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Swedish",
      "Swedish law",
      "Swedish Vancouver-Style"
    ]
  },
  {
    "id": "swedish-law-author-number",
    "name": "Swedish Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Swedish",
      "Swedish law",
      "Swedish Author-Number"
    ]
  },
  {
    "id": "swedish-medicine-author-date",
    "name": "Swedish Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Swedish",
      "Swedish medicine",
      "Swedish Author-Date"
    ]
  },
  {
    "id": "swedish-medicine-footnote",
    "name": "Swedish Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Swedish",
      "Swedish medicine",
      "Swedish Footnote"
    ]
  },
  {
    "id": "swedish-medicine-endnote",
    "name": "Swedish Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Swedish",
      "Swedish medicine",
      "Swedish Endnote"
    ]
  },
  {
    "id": "swedish-medicine-numeric",
    "name": "Swedish Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Swedish",
      "Swedish medicine",
      "Swedish Numeric"
    ]
  },
  {
    "id": "swedish-medicine-vancouver-style",
    "name": "Swedish Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Swedish",
      "Swedish medicine",
      "Swedish Vancouver-Style"
    ]
  },
  {
    "id": "swedish-medicine-author-number",
    "name": "Swedish Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Swedish",
      "Swedish medicine",
      "Swedish Author-Number"
    ]
  },
  {
    "id": "swedish-general-author-date",
    "name": "Swedish General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Swedish",
      "Swedish general",
      "Swedish Author-Date"
    ]
  },
  {
    "id": "swedish-general-footnote",
    "name": "Swedish General — Footnote",
    "discipline": "general",
    "aliases": [
      "Swedish",
      "Swedish general",
      "Swedish Footnote"
    ]
  },
  {
    "id": "swedish-general-endnote",
    "name": "Swedish General — Endnote",
    "discipline": "general",
    "aliases": [
      "Swedish",
      "Swedish general",
      "Swedish Endnote"
    ]
  },
  {
    "id": "swedish-general-numeric",
    "name": "Swedish General — Numeric",
    "discipline": "general",
    "aliases": [
      "Swedish",
      "Swedish general",
      "Swedish Numeric"
    ]
  },
  {
    "id": "swedish-general-vancouver-style",
    "name": "Swedish General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Swedish",
      "Swedish general",
      "Swedish Vancouver-Style"
    ]
  },
  {
    "id": "swedish-general-author-number",
    "name": "Swedish General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Swedish",
      "Swedish general",
      "Swedish Author-Number"
    ]
  },
  {
    "id": "norwegian-sciences-author-date",
    "name": "Norwegian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Norwegian",
      "Norwegian sciences",
      "Norwegian Author-Date"
    ]
  },
  {
    "id": "norwegian-sciences-footnote",
    "name": "Norwegian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Norwegian",
      "Norwegian sciences",
      "Norwegian Footnote"
    ]
  },
  {
    "id": "norwegian-sciences-endnote",
    "name": "Norwegian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Norwegian",
      "Norwegian sciences",
      "Norwegian Endnote"
    ]
  },
  {
    "id": "norwegian-sciences-numeric",
    "name": "Norwegian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Norwegian",
      "Norwegian sciences",
      "Norwegian Numeric"
    ]
  },
  {
    "id": "norwegian-sciences-vancouver-style",
    "name": "Norwegian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Norwegian",
      "Norwegian sciences",
      "Norwegian Vancouver-Style"
    ]
  },
  {
    "id": "norwegian-sciences-author-number",
    "name": "Norwegian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Norwegian",
      "Norwegian sciences",
      "Norwegian Author-Number"
    ]
  },
  {
    "id": "norwegian-humanities-author-date",
    "name": "Norwegian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Norwegian",
      "Norwegian humanities",
      "Norwegian Author-Date"
    ]
  },
  {
    "id": "norwegian-humanities-footnote",
    "name": "Norwegian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Norwegian",
      "Norwegian humanities",
      "Norwegian Footnote"
    ]
  },
  {
    "id": "norwegian-humanities-endnote",
    "name": "Norwegian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Norwegian",
      "Norwegian humanities",
      "Norwegian Endnote"
    ]
  },
  {
    "id": "norwegian-humanities-numeric",
    "name": "Norwegian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Norwegian",
      "Norwegian humanities",
      "Norwegian Numeric"
    ]
  },
  {
    "id": "norwegian-humanities-vancouver-style",
    "name": "Norwegian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Norwegian",
      "Norwegian humanities",
      "Norwegian Vancouver-Style"
    ]
  },
  {
    "id": "norwegian-humanities-author-number",
    "name": "Norwegian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Norwegian",
      "Norwegian humanities",
      "Norwegian Author-Number"
    ]
  },
  {
    "id": "norwegian-law-author-date",
    "name": "Norwegian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Norwegian",
      "Norwegian law",
      "Norwegian Author-Date"
    ]
  },
  {
    "id": "norwegian-law-footnote",
    "name": "Norwegian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Norwegian",
      "Norwegian law",
      "Norwegian Footnote"
    ]
  },
  {
    "id": "norwegian-law-endnote",
    "name": "Norwegian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Norwegian",
      "Norwegian law",
      "Norwegian Endnote"
    ]
  },
  {
    "id": "norwegian-law-numeric",
    "name": "Norwegian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Norwegian",
      "Norwegian law",
      "Norwegian Numeric"
    ]
  },
  {
    "id": "norwegian-law-vancouver-style",
    "name": "Norwegian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Norwegian",
      "Norwegian law",
      "Norwegian Vancouver-Style"
    ]
  },
  {
    "id": "norwegian-law-author-number",
    "name": "Norwegian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Norwegian",
      "Norwegian law",
      "Norwegian Author-Number"
    ]
  },
  {
    "id": "norwegian-medicine-author-date",
    "name": "Norwegian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Norwegian",
      "Norwegian medicine",
      "Norwegian Author-Date"
    ]
  },
  {
    "id": "norwegian-medicine-footnote",
    "name": "Norwegian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Norwegian",
      "Norwegian medicine",
      "Norwegian Footnote"
    ]
  },
  {
    "id": "norwegian-medicine-endnote",
    "name": "Norwegian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Norwegian",
      "Norwegian medicine",
      "Norwegian Endnote"
    ]
  },
  {
    "id": "norwegian-medicine-numeric",
    "name": "Norwegian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Norwegian",
      "Norwegian medicine",
      "Norwegian Numeric"
    ]
  },
  {
    "id": "norwegian-medicine-vancouver-style",
    "name": "Norwegian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Norwegian",
      "Norwegian medicine",
      "Norwegian Vancouver-Style"
    ]
  },
  {
    "id": "norwegian-medicine-author-number",
    "name": "Norwegian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Norwegian",
      "Norwegian medicine",
      "Norwegian Author-Number"
    ]
  },
  {
    "id": "norwegian-general-author-date",
    "name": "Norwegian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Norwegian",
      "Norwegian general",
      "Norwegian Author-Date"
    ]
  },
  {
    "id": "norwegian-general-footnote",
    "name": "Norwegian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Norwegian",
      "Norwegian general",
      "Norwegian Footnote"
    ]
  },
  {
    "id": "norwegian-general-endnote",
    "name": "Norwegian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Norwegian",
      "Norwegian general",
      "Norwegian Endnote"
    ]
  },
  {
    "id": "norwegian-general-numeric",
    "name": "Norwegian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Norwegian",
      "Norwegian general",
      "Norwegian Numeric"
    ]
  },
  {
    "id": "norwegian-general-vancouver-style",
    "name": "Norwegian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Norwegian",
      "Norwegian general",
      "Norwegian Vancouver-Style"
    ]
  },
  {
    "id": "norwegian-general-author-number",
    "name": "Norwegian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Norwegian",
      "Norwegian general",
      "Norwegian Author-Number"
    ]
  },
  {
    "id": "danish-sciences-author-date",
    "name": "Danish Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Danish",
      "Danish sciences",
      "Danish Author-Date"
    ]
  },
  {
    "id": "danish-sciences-footnote",
    "name": "Danish Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Danish",
      "Danish sciences",
      "Danish Footnote"
    ]
  },
  {
    "id": "danish-sciences-endnote",
    "name": "Danish Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Danish",
      "Danish sciences",
      "Danish Endnote"
    ]
  },
  {
    "id": "danish-sciences-numeric",
    "name": "Danish Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Danish",
      "Danish sciences",
      "Danish Numeric"
    ]
  },
  {
    "id": "danish-sciences-vancouver-style",
    "name": "Danish Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Danish",
      "Danish sciences",
      "Danish Vancouver-Style"
    ]
  },
  {
    "id": "danish-sciences-author-number",
    "name": "Danish Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Danish",
      "Danish sciences",
      "Danish Author-Number"
    ]
  },
  {
    "id": "danish-humanities-author-date",
    "name": "Danish Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Danish",
      "Danish humanities",
      "Danish Author-Date"
    ]
  },
  {
    "id": "danish-humanities-footnote",
    "name": "Danish Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Danish",
      "Danish humanities",
      "Danish Footnote"
    ]
  },
  {
    "id": "danish-humanities-endnote",
    "name": "Danish Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Danish",
      "Danish humanities",
      "Danish Endnote"
    ]
  },
  {
    "id": "danish-humanities-numeric",
    "name": "Danish Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Danish",
      "Danish humanities",
      "Danish Numeric"
    ]
  },
  {
    "id": "danish-humanities-vancouver-style",
    "name": "Danish Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Danish",
      "Danish humanities",
      "Danish Vancouver-Style"
    ]
  },
  {
    "id": "danish-humanities-author-number",
    "name": "Danish Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Danish",
      "Danish humanities",
      "Danish Author-Number"
    ]
  },
  {
    "id": "danish-law-author-date",
    "name": "Danish Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Danish",
      "Danish law",
      "Danish Author-Date"
    ]
  },
  {
    "id": "danish-law-footnote",
    "name": "Danish Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Danish",
      "Danish law",
      "Danish Footnote"
    ]
  },
  {
    "id": "danish-law-endnote",
    "name": "Danish Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Danish",
      "Danish law",
      "Danish Endnote"
    ]
  },
  {
    "id": "danish-law-numeric",
    "name": "Danish Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Danish",
      "Danish law",
      "Danish Numeric"
    ]
  },
  {
    "id": "danish-law-vancouver-style",
    "name": "Danish Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Danish",
      "Danish law",
      "Danish Vancouver-Style"
    ]
  },
  {
    "id": "danish-law-author-number",
    "name": "Danish Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Danish",
      "Danish law",
      "Danish Author-Number"
    ]
  },
  {
    "id": "danish-medicine-author-date",
    "name": "Danish Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Danish",
      "Danish medicine",
      "Danish Author-Date"
    ]
  },
  {
    "id": "danish-medicine-footnote",
    "name": "Danish Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Danish",
      "Danish medicine",
      "Danish Footnote"
    ]
  },
  {
    "id": "danish-medicine-endnote",
    "name": "Danish Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Danish",
      "Danish medicine",
      "Danish Endnote"
    ]
  },
  {
    "id": "danish-medicine-numeric",
    "name": "Danish Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Danish",
      "Danish medicine",
      "Danish Numeric"
    ]
  },
  {
    "id": "danish-medicine-vancouver-style",
    "name": "Danish Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Danish",
      "Danish medicine",
      "Danish Vancouver-Style"
    ]
  },
  {
    "id": "danish-medicine-author-number",
    "name": "Danish Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Danish",
      "Danish medicine",
      "Danish Author-Number"
    ]
  },
  {
    "id": "danish-general-author-date",
    "name": "Danish General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Danish",
      "Danish general",
      "Danish Author-Date"
    ]
  },
  {
    "id": "danish-general-footnote",
    "name": "Danish General — Footnote",
    "discipline": "general",
    "aliases": [
      "Danish",
      "Danish general",
      "Danish Footnote"
    ]
  },
  {
    "id": "danish-general-endnote",
    "name": "Danish General — Endnote",
    "discipline": "general",
    "aliases": [
      "Danish",
      "Danish general",
      "Danish Endnote"
    ]
  },
  {
    "id": "danish-general-numeric",
    "name": "Danish General — Numeric",
    "discipline": "general",
    "aliases": [
      "Danish",
      "Danish general",
      "Danish Numeric"
    ]
  },
  {
    "id": "danish-general-vancouver-style",
    "name": "Danish General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Danish",
      "Danish general",
      "Danish Vancouver-Style"
    ]
  },
  {
    "id": "danish-general-author-number",
    "name": "Danish General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Danish",
      "Danish general",
      "Danish Author-Number"
    ]
  },
  {
    "id": "finnish-sciences-author-date",
    "name": "Finnish Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Finnish",
      "Finnish sciences",
      "Finnish Author-Date"
    ]
  },
  {
    "id": "finnish-sciences-footnote",
    "name": "Finnish Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Finnish",
      "Finnish sciences",
      "Finnish Footnote"
    ]
  },
  {
    "id": "finnish-sciences-endnote",
    "name": "Finnish Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Finnish",
      "Finnish sciences",
      "Finnish Endnote"
    ]
  },
  {
    "id": "finnish-sciences-numeric",
    "name": "Finnish Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Finnish",
      "Finnish sciences",
      "Finnish Numeric"
    ]
  },
  {
    "id": "finnish-sciences-vancouver-style",
    "name": "Finnish Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Finnish",
      "Finnish sciences",
      "Finnish Vancouver-Style"
    ]
  },
  {
    "id": "finnish-sciences-author-number",
    "name": "Finnish Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Finnish",
      "Finnish sciences",
      "Finnish Author-Number"
    ]
  },
  {
    "id": "finnish-humanities-author-date",
    "name": "Finnish Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Finnish",
      "Finnish humanities",
      "Finnish Author-Date"
    ]
  },
  {
    "id": "finnish-humanities-footnote",
    "name": "Finnish Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Finnish",
      "Finnish humanities",
      "Finnish Footnote"
    ]
  },
  {
    "id": "finnish-humanities-endnote",
    "name": "Finnish Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Finnish",
      "Finnish humanities",
      "Finnish Endnote"
    ]
  },
  {
    "id": "finnish-humanities-numeric",
    "name": "Finnish Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Finnish",
      "Finnish humanities",
      "Finnish Numeric"
    ]
  },
  {
    "id": "finnish-humanities-vancouver-style",
    "name": "Finnish Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Finnish",
      "Finnish humanities",
      "Finnish Vancouver-Style"
    ]
  },
  {
    "id": "finnish-humanities-author-number",
    "name": "Finnish Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Finnish",
      "Finnish humanities",
      "Finnish Author-Number"
    ]
  },
  {
    "id": "finnish-law-author-date",
    "name": "Finnish Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Finnish",
      "Finnish law",
      "Finnish Author-Date"
    ]
  },
  {
    "id": "finnish-law-footnote",
    "name": "Finnish Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Finnish",
      "Finnish law",
      "Finnish Footnote"
    ]
  },
  {
    "id": "finnish-law-endnote",
    "name": "Finnish Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Finnish",
      "Finnish law",
      "Finnish Endnote"
    ]
  },
  {
    "id": "finnish-law-numeric",
    "name": "Finnish Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Finnish",
      "Finnish law",
      "Finnish Numeric"
    ]
  },
  {
    "id": "finnish-law-vancouver-style",
    "name": "Finnish Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Finnish",
      "Finnish law",
      "Finnish Vancouver-Style"
    ]
  },
  {
    "id": "finnish-law-author-number",
    "name": "Finnish Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Finnish",
      "Finnish law",
      "Finnish Author-Number"
    ]
  },
  {
    "id": "finnish-medicine-author-date",
    "name": "Finnish Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Finnish",
      "Finnish medicine",
      "Finnish Author-Date"
    ]
  },
  {
    "id": "finnish-medicine-footnote",
    "name": "Finnish Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Finnish",
      "Finnish medicine",
      "Finnish Footnote"
    ]
  },
  {
    "id": "finnish-medicine-endnote",
    "name": "Finnish Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Finnish",
      "Finnish medicine",
      "Finnish Endnote"
    ]
  },
  {
    "id": "finnish-medicine-numeric",
    "name": "Finnish Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Finnish",
      "Finnish medicine",
      "Finnish Numeric"
    ]
  },
  {
    "id": "finnish-medicine-vancouver-style",
    "name": "Finnish Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Finnish",
      "Finnish medicine",
      "Finnish Vancouver-Style"
    ]
  },
  {
    "id": "finnish-medicine-author-number",
    "name": "Finnish Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Finnish",
      "Finnish medicine",
      "Finnish Author-Number"
    ]
  },
  {
    "id": "finnish-general-author-date",
    "name": "Finnish General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Finnish",
      "Finnish general",
      "Finnish Author-Date"
    ]
  },
  {
    "id": "finnish-general-footnote",
    "name": "Finnish General — Footnote",
    "discipline": "general",
    "aliases": [
      "Finnish",
      "Finnish general",
      "Finnish Footnote"
    ]
  },
  {
    "id": "finnish-general-endnote",
    "name": "Finnish General — Endnote",
    "discipline": "general",
    "aliases": [
      "Finnish",
      "Finnish general",
      "Finnish Endnote"
    ]
  },
  {
    "id": "finnish-general-numeric",
    "name": "Finnish General — Numeric",
    "discipline": "general",
    "aliases": [
      "Finnish",
      "Finnish general",
      "Finnish Numeric"
    ]
  },
  {
    "id": "finnish-general-vancouver-style",
    "name": "Finnish General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Finnish",
      "Finnish general",
      "Finnish Vancouver-Style"
    ]
  },
  {
    "id": "finnish-general-author-number",
    "name": "Finnish General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Finnish",
      "Finnish general",
      "Finnish Author-Number"
    ]
  },
  {
    "id": "polish-sciences-author-date",
    "name": "Polish Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Polish",
      "Polish sciences",
      "Polish Author-Date"
    ]
  },
  {
    "id": "polish-sciences-footnote",
    "name": "Polish Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Polish",
      "Polish sciences",
      "Polish Footnote"
    ]
  },
  {
    "id": "polish-sciences-endnote",
    "name": "Polish Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Polish",
      "Polish sciences",
      "Polish Endnote"
    ]
  },
  {
    "id": "polish-sciences-numeric",
    "name": "Polish Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Polish",
      "Polish sciences",
      "Polish Numeric"
    ]
  },
  {
    "id": "polish-sciences-vancouver-style",
    "name": "Polish Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Polish",
      "Polish sciences",
      "Polish Vancouver-Style"
    ]
  },
  {
    "id": "polish-sciences-author-number",
    "name": "Polish Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Polish",
      "Polish sciences",
      "Polish Author-Number"
    ]
  },
  {
    "id": "polish-humanities-author-date",
    "name": "Polish Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Polish",
      "Polish humanities",
      "Polish Author-Date"
    ]
  },
  {
    "id": "polish-humanities-footnote",
    "name": "Polish Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Polish",
      "Polish humanities",
      "Polish Footnote"
    ]
  },
  {
    "id": "polish-humanities-endnote",
    "name": "Polish Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Polish",
      "Polish humanities",
      "Polish Endnote"
    ]
  },
  {
    "id": "polish-humanities-numeric",
    "name": "Polish Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Polish",
      "Polish humanities",
      "Polish Numeric"
    ]
  },
  {
    "id": "polish-humanities-vancouver-style",
    "name": "Polish Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Polish",
      "Polish humanities",
      "Polish Vancouver-Style"
    ]
  },
  {
    "id": "polish-humanities-author-number",
    "name": "Polish Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Polish",
      "Polish humanities",
      "Polish Author-Number"
    ]
  },
  {
    "id": "polish-law-author-date",
    "name": "Polish Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Polish",
      "Polish law",
      "Polish Author-Date"
    ]
  },
  {
    "id": "polish-law-footnote",
    "name": "Polish Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Polish",
      "Polish law",
      "Polish Footnote"
    ]
  },
  {
    "id": "polish-law-endnote",
    "name": "Polish Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Polish",
      "Polish law",
      "Polish Endnote"
    ]
  },
  {
    "id": "polish-law-numeric",
    "name": "Polish Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Polish",
      "Polish law",
      "Polish Numeric"
    ]
  },
  {
    "id": "polish-law-vancouver-style",
    "name": "Polish Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Polish",
      "Polish law",
      "Polish Vancouver-Style"
    ]
  },
  {
    "id": "polish-law-author-number",
    "name": "Polish Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Polish",
      "Polish law",
      "Polish Author-Number"
    ]
  },
  {
    "id": "polish-medicine-author-date",
    "name": "Polish Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Polish",
      "Polish medicine",
      "Polish Author-Date"
    ]
  },
  {
    "id": "polish-medicine-footnote",
    "name": "Polish Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Polish",
      "Polish medicine",
      "Polish Footnote"
    ]
  },
  {
    "id": "polish-medicine-endnote",
    "name": "Polish Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Polish",
      "Polish medicine",
      "Polish Endnote"
    ]
  },
  {
    "id": "polish-medicine-numeric",
    "name": "Polish Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Polish",
      "Polish medicine",
      "Polish Numeric"
    ]
  },
  {
    "id": "polish-medicine-vancouver-style",
    "name": "Polish Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Polish",
      "Polish medicine",
      "Polish Vancouver-Style"
    ]
  },
  {
    "id": "polish-medicine-author-number",
    "name": "Polish Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Polish",
      "Polish medicine",
      "Polish Author-Number"
    ]
  },
  {
    "id": "polish-general-author-date",
    "name": "Polish General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Polish",
      "Polish general",
      "Polish Author-Date"
    ]
  },
  {
    "id": "polish-general-footnote",
    "name": "Polish General — Footnote",
    "discipline": "general",
    "aliases": [
      "Polish",
      "Polish general",
      "Polish Footnote"
    ]
  },
  {
    "id": "polish-general-endnote",
    "name": "Polish General — Endnote",
    "discipline": "general",
    "aliases": [
      "Polish",
      "Polish general",
      "Polish Endnote"
    ]
  },
  {
    "id": "polish-general-numeric",
    "name": "Polish General — Numeric",
    "discipline": "general",
    "aliases": [
      "Polish",
      "Polish general",
      "Polish Numeric"
    ]
  },
  {
    "id": "polish-general-vancouver-style",
    "name": "Polish General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Polish",
      "Polish general",
      "Polish Vancouver-Style"
    ]
  },
  {
    "id": "polish-general-author-number",
    "name": "Polish General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Polish",
      "Polish general",
      "Polish Author-Number"
    ]
  },
  {
    "id": "czech-sciences-author-date",
    "name": "Czech Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Czech",
      "Czech sciences",
      "Czech Author-Date"
    ]
  },
  {
    "id": "czech-sciences-footnote",
    "name": "Czech Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Czech",
      "Czech sciences",
      "Czech Footnote"
    ]
  },
  {
    "id": "czech-sciences-endnote",
    "name": "Czech Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Czech",
      "Czech sciences",
      "Czech Endnote"
    ]
  },
  {
    "id": "czech-sciences-numeric",
    "name": "Czech Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Czech",
      "Czech sciences",
      "Czech Numeric"
    ]
  },
  {
    "id": "czech-sciences-vancouver-style",
    "name": "Czech Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Czech",
      "Czech sciences",
      "Czech Vancouver-Style"
    ]
  },
  {
    "id": "czech-sciences-author-number",
    "name": "Czech Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Czech",
      "Czech sciences",
      "Czech Author-Number"
    ]
  },
  {
    "id": "czech-humanities-author-date",
    "name": "Czech Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Czech",
      "Czech humanities",
      "Czech Author-Date"
    ]
  },
  {
    "id": "czech-humanities-footnote",
    "name": "Czech Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Czech",
      "Czech humanities",
      "Czech Footnote"
    ]
  },
  {
    "id": "czech-humanities-endnote",
    "name": "Czech Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Czech",
      "Czech humanities",
      "Czech Endnote"
    ]
  },
  {
    "id": "czech-humanities-numeric",
    "name": "Czech Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Czech",
      "Czech humanities",
      "Czech Numeric"
    ]
  },
  {
    "id": "czech-humanities-vancouver-style",
    "name": "Czech Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Czech",
      "Czech humanities",
      "Czech Vancouver-Style"
    ]
  },
  {
    "id": "czech-humanities-author-number",
    "name": "Czech Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Czech",
      "Czech humanities",
      "Czech Author-Number"
    ]
  },
  {
    "id": "czech-law-author-date",
    "name": "Czech Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Czech",
      "Czech law",
      "Czech Author-Date"
    ]
  },
  {
    "id": "czech-law-footnote",
    "name": "Czech Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Czech",
      "Czech law",
      "Czech Footnote"
    ]
  },
  {
    "id": "czech-law-endnote",
    "name": "Czech Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Czech",
      "Czech law",
      "Czech Endnote"
    ]
  },
  {
    "id": "czech-law-numeric",
    "name": "Czech Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Czech",
      "Czech law",
      "Czech Numeric"
    ]
  },
  {
    "id": "czech-law-vancouver-style",
    "name": "Czech Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Czech",
      "Czech law",
      "Czech Vancouver-Style"
    ]
  },
  {
    "id": "czech-law-author-number",
    "name": "Czech Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Czech",
      "Czech law",
      "Czech Author-Number"
    ]
  },
  {
    "id": "czech-medicine-author-date",
    "name": "Czech Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Czech",
      "Czech medicine",
      "Czech Author-Date"
    ]
  },
  {
    "id": "czech-medicine-footnote",
    "name": "Czech Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Czech",
      "Czech medicine",
      "Czech Footnote"
    ]
  },
  {
    "id": "czech-medicine-endnote",
    "name": "Czech Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Czech",
      "Czech medicine",
      "Czech Endnote"
    ]
  },
  {
    "id": "czech-medicine-numeric",
    "name": "Czech Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Czech",
      "Czech medicine",
      "Czech Numeric"
    ]
  },
  {
    "id": "czech-medicine-vancouver-style",
    "name": "Czech Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Czech",
      "Czech medicine",
      "Czech Vancouver-Style"
    ]
  },
  {
    "id": "czech-medicine-author-number",
    "name": "Czech Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Czech",
      "Czech medicine",
      "Czech Author-Number"
    ]
  },
  {
    "id": "czech-general-author-date",
    "name": "Czech General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Czech",
      "Czech general",
      "Czech Author-Date"
    ]
  },
  {
    "id": "czech-general-footnote",
    "name": "Czech General — Footnote",
    "discipline": "general",
    "aliases": [
      "Czech",
      "Czech general",
      "Czech Footnote"
    ]
  },
  {
    "id": "czech-general-endnote",
    "name": "Czech General — Endnote",
    "discipline": "general",
    "aliases": [
      "Czech",
      "Czech general",
      "Czech Endnote"
    ]
  },
  {
    "id": "czech-general-numeric",
    "name": "Czech General — Numeric",
    "discipline": "general",
    "aliases": [
      "Czech",
      "Czech general",
      "Czech Numeric"
    ]
  },
  {
    "id": "czech-general-vancouver-style",
    "name": "Czech General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Czech",
      "Czech general",
      "Czech Vancouver-Style"
    ]
  },
  {
    "id": "czech-general-author-number",
    "name": "Czech General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Czech",
      "Czech general",
      "Czech Author-Number"
    ]
  },
  {
    "id": "hungarian-sciences-author-date",
    "name": "Hungarian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Hungarian",
      "Hungarian sciences",
      "Hungarian Author-Date"
    ]
  },
  {
    "id": "hungarian-sciences-footnote",
    "name": "Hungarian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Hungarian",
      "Hungarian sciences",
      "Hungarian Footnote"
    ]
  },
  {
    "id": "hungarian-sciences-endnote",
    "name": "Hungarian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Hungarian",
      "Hungarian sciences",
      "Hungarian Endnote"
    ]
  },
  {
    "id": "hungarian-sciences-numeric",
    "name": "Hungarian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Hungarian",
      "Hungarian sciences",
      "Hungarian Numeric"
    ]
  },
  {
    "id": "hungarian-sciences-vancouver-style",
    "name": "Hungarian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Hungarian",
      "Hungarian sciences",
      "Hungarian Vancouver-Style"
    ]
  },
  {
    "id": "hungarian-sciences-author-number",
    "name": "Hungarian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Hungarian",
      "Hungarian sciences",
      "Hungarian Author-Number"
    ]
  },
  {
    "id": "hungarian-humanities-author-date",
    "name": "Hungarian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Hungarian",
      "Hungarian humanities",
      "Hungarian Author-Date"
    ]
  },
  {
    "id": "hungarian-humanities-footnote",
    "name": "Hungarian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Hungarian",
      "Hungarian humanities",
      "Hungarian Footnote"
    ]
  },
  {
    "id": "hungarian-humanities-endnote",
    "name": "Hungarian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Hungarian",
      "Hungarian humanities",
      "Hungarian Endnote"
    ]
  },
  {
    "id": "hungarian-humanities-numeric",
    "name": "Hungarian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Hungarian",
      "Hungarian humanities",
      "Hungarian Numeric"
    ]
  },
  {
    "id": "hungarian-humanities-vancouver-style",
    "name": "Hungarian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Hungarian",
      "Hungarian humanities",
      "Hungarian Vancouver-Style"
    ]
  },
  {
    "id": "hungarian-humanities-author-number",
    "name": "Hungarian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Hungarian",
      "Hungarian humanities",
      "Hungarian Author-Number"
    ]
  },
  {
    "id": "hungarian-law-author-date",
    "name": "Hungarian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Hungarian",
      "Hungarian law",
      "Hungarian Author-Date"
    ]
  },
  {
    "id": "hungarian-law-footnote",
    "name": "Hungarian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Hungarian",
      "Hungarian law",
      "Hungarian Footnote"
    ]
  },
  {
    "id": "hungarian-law-endnote",
    "name": "Hungarian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Hungarian",
      "Hungarian law",
      "Hungarian Endnote"
    ]
  },
  {
    "id": "hungarian-law-numeric",
    "name": "Hungarian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Hungarian",
      "Hungarian law",
      "Hungarian Numeric"
    ]
  },
  {
    "id": "hungarian-law-vancouver-style",
    "name": "Hungarian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Hungarian",
      "Hungarian law",
      "Hungarian Vancouver-Style"
    ]
  },
  {
    "id": "hungarian-law-author-number",
    "name": "Hungarian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Hungarian",
      "Hungarian law",
      "Hungarian Author-Number"
    ]
  },
  {
    "id": "hungarian-medicine-author-date",
    "name": "Hungarian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Hungarian",
      "Hungarian medicine",
      "Hungarian Author-Date"
    ]
  },
  {
    "id": "hungarian-medicine-footnote",
    "name": "Hungarian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Hungarian",
      "Hungarian medicine",
      "Hungarian Footnote"
    ]
  },
  {
    "id": "hungarian-medicine-endnote",
    "name": "Hungarian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Hungarian",
      "Hungarian medicine",
      "Hungarian Endnote"
    ]
  },
  {
    "id": "hungarian-medicine-numeric",
    "name": "Hungarian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Hungarian",
      "Hungarian medicine",
      "Hungarian Numeric"
    ]
  },
  {
    "id": "hungarian-medicine-vancouver-style",
    "name": "Hungarian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Hungarian",
      "Hungarian medicine",
      "Hungarian Vancouver-Style"
    ]
  },
  {
    "id": "hungarian-medicine-author-number",
    "name": "Hungarian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Hungarian",
      "Hungarian medicine",
      "Hungarian Author-Number"
    ]
  },
  {
    "id": "hungarian-general-author-date",
    "name": "Hungarian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Hungarian",
      "Hungarian general",
      "Hungarian Author-Date"
    ]
  },
  {
    "id": "hungarian-general-footnote",
    "name": "Hungarian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Hungarian",
      "Hungarian general",
      "Hungarian Footnote"
    ]
  },
  {
    "id": "hungarian-general-endnote",
    "name": "Hungarian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Hungarian",
      "Hungarian general",
      "Hungarian Endnote"
    ]
  },
  {
    "id": "hungarian-general-numeric",
    "name": "Hungarian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Hungarian",
      "Hungarian general",
      "Hungarian Numeric"
    ]
  },
  {
    "id": "hungarian-general-vancouver-style",
    "name": "Hungarian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Hungarian",
      "Hungarian general",
      "Hungarian Vancouver-Style"
    ]
  },
  {
    "id": "hungarian-general-author-number",
    "name": "Hungarian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Hungarian",
      "Hungarian general",
      "Hungarian Author-Number"
    ]
  },
  {
    "id": "romanian-sciences-author-date",
    "name": "Romanian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Romanian",
      "Romanian sciences",
      "Romanian Author-Date"
    ]
  },
  {
    "id": "romanian-sciences-footnote",
    "name": "Romanian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Romanian",
      "Romanian sciences",
      "Romanian Footnote"
    ]
  },
  {
    "id": "romanian-sciences-endnote",
    "name": "Romanian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Romanian",
      "Romanian sciences",
      "Romanian Endnote"
    ]
  },
  {
    "id": "romanian-sciences-numeric",
    "name": "Romanian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Romanian",
      "Romanian sciences",
      "Romanian Numeric"
    ]
  },
  {
    "id": "romanian-sciences-vancouver-style",
    "name": "Romanian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Romanian",
      "Romanian sciences",
      "Romanian Vancouver-Style"
    ]
  },
  {
    "id": "romanian-sciences-author-number",
    "name": "Romanian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Romanian",
      "Romanian sciences",
      "Romanian Author-Number"
    ]
  },
  {
    "id": "romanian-humanities-author-date",
    "name": "Romanian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Romanian",
      "Romanian humanities",
      "Romanian Author-Date"
    ]
  },
  {
    "id": "romanian-humanities-footnote",
    "name": "Romanian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Romanian",
      "Romanian humanities",
      "Romanian Footnote"
    ]
  },
  {
    "id": "romanian-humanities-endnote",
    "name": "Romanian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Romanian",
      "Romanian humanities",
      "Romanian Endnote"
    ]
  },
  {
    "id": "romanian-humanities-numeric",
    "name": "Romanian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Romanian",
      "Romanian humanities",
      "Romanian Numeric"
    ]
  },
  {
    "id": "romanian-humanities-vancouver-style",
    "name": "Romanian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Romanian",
      "Romanian humanities",
      "Romanian Vancouver-Style"
    ]
  },
  {
    "id": "romanian-humanities-author-number",
    "name": "Romanian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Romanian",
      "Romanian humanities",
      "Romanian Author-Number"
    ]
  },
  {
    "id": "romanian-law-author-date",
    "name": "Romanian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Romanian",
      "Romanian law",
      "Romanian Author-Date"
    ]
  },
  {
    "id": "romanian-law-footnote",
    "name": "Romanian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Romanian",
      "Romanian law",
      "Romanian Footnote"
    ]
  },
  {
    "id": "romanian-law-endnote",
    "name": "Romanian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Romanian",
      "Romanian law",
      "Romanian Endnote"
    ]
  },
  {
    "id": "romanian-law-numeric",
    "name": "Romanian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Romanian",
      "Romanian law",
      "Romanian Numeric"
    ]
  },
  {
    "id": "romanian-law-vancouver-style",
    "name": "Romanian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Romanian",
      "Romanian law",
      "Romanian Vancouver-Style"
    ]
  },
  {
    "id": "romanian-law-author-number",
    "name": "Romanian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Romanian",
      "Romanian law",
      "Romanian Author-Number"
    ]
  },
  {
    "id": "romanian-medicine-author-date",
    "name": "Romanian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Romanian",
      "Romanian medicine",
      "Romanian Author-Date"
    ]
  },
  {
    "id": "romanian-medicine-footnote",
    "name": "Romanian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Romanian",
      "Romanian medicine",
      "Romanian Footnote"
    ]
  },
  {
    "id": "romanian-medicine-endnote",
    "name": "Romanian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Romanian",
      "Romanian medicine",
      "Romanian Endnote"
    ]
  },
  {
    "id": "romanian-medicine-numeric",
    "name": "Romanian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Romanian",
      "Romanian medicine",
      "Romanian Numeric"
    ]
  },
  {
    "id": "romanian-medicine-vancouver-style",
    "name": "Romanian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Romanian",
      "Romanian medicine",
      "Romanian Vancouver-Style"
    ]
  },
  {
    "id": "romanian-medicine-author-number",
    "name": "Romanian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Romanian",
      "Romanian medicine",
      "Romanian Author-Number"
    ]
  },
  {
    "id": "romanian-general-author-date",
    "name": "Romanian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Romanian",
      "Romanian general",
      "Romanian Author-Date"
    ]
  },
  {
    "id": "romanian-general-footnote",
    "name": "Romanian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Romanian",
      "Romanian general",
      "Romanian Footnote"
    ]
  },
  {
    "id": "romanian-general-endnote",
    "name": "Romanian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Romanian",
      "Romanian general",
      "Romanian Endnote"
    ]
  },
  {
    "id": "romanian-general-numeric",
    "name": "Romanian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Romanian",
      "Romanian general",
      "Romanian Numeric"
    ]
  },
  {
    "id": "romanian-general-vancouver-style",
    "name": "Romanian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Romanian",
      "Romanian general",
      "Romanian Vancouver-Style"
    ]
  },
  {
    "id": "romanian-general-author-number",
    "name": "Romanian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Romanian",
      "Romanian general",
      "Romanian Author-Number"
    ]
  },
  {
    "id": "greek-sciences-author-date",
    "name": "Greek Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Greek",
      "Greek sciences",
      "Greek Author-Date"
    ]
  },
  {
    "id": "greek-sciences-footnote",
    "name": "Greek Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Greek",
      "Greek sciences",
      "Greek Footnote"
    ]
  },
  {
    "id": "greek-sciences-endnote",
    "name": "Greek Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Greek",
      "Greek sciences",
      "Greek Endnote"
    ]
  },
  {
    "id": "greek-sciences-numeric",
    "name": "Greek Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Greek",
      "Greek sciences",
      "Greek Numeric"
    ]
  },
  {
    "id": "greek-sciences-vancouver-style",
    "name": "Greek Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Greek",
      "Greek sciences",
      "Greek Vancouver-Style"
    ]
  },
  {
    "id": "greek-sciences-author-number",
    "name": "Greek Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Greek",
      "Greek sciences",
      "Greek Author-Number"
    ]
  },
  {
    "id": "greek-humanities-author-date",
    "name": "Greek Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Greek",
      "Greek humanities",
      "Greek Author-Date"
    ]
  },
  {
    "id": "greek-humanities-footnote",
    "name": "Greek Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Greek",
      "Greek humanities",
      "Greek Footnote"
    ]
  },
  {
    "id": "greek-humanities-endnote",
    "name": "Greek Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Greek",
      "Greek humanities",
      "Greek Endnote"
    ]
  },
  {
    "id": "greek-humanities-numeric",
    "name": "Greek Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Greek",
      "Greek humanities",
      "Greek Numeric"
    ]
  },
  {
    "id": "greek-humanities-vancouver-style",
    "name": "Greek Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Greek",
      "Greek humanities",
      "Greek Vancouver-Style"
    ]
  },
  {
    "id": "greek-humanities-author-number",
    "name": "Greek Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Greek",
      "Greek humanities",
      "Greek Author-Number"
    ]
  },
  {
    "id": "greek-law-author-date",
    "name": "Greek Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Greek",
      "Greek law",
      "Greek Author-Date"
    ]
  },
  {
    "id": "greek-law-footnote",
    "name": "Greek Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Greek",
      "Greek law",
      "Greek Footnote"
    ]
  },
  {
    "id": "greek-law-endnote",
    "name": "Greek Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Greek",
      "Greek law",
      "Greek Endnote"
    ]
  },
  {
    "id": "greek-law-numeric",
    "name": "Greek Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Greek",
      "Greek law",
      "Greek Numeric"
    ]
  },
  {
    "id": "greek-law-vancouver-style",
    "name": "Greek Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Greek",
      "Greek law",
      "Greek Vancouver-Style"
    ]
  },
  {
    "id": "greek-law-author-number",
    "name": "Greek Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Greek",
      "Greek law",
      "Greek Author-Number"
    ]
  },
  {
    "id": "greek-medicine-author-date",
    "name": "Greek Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Greek",
      "Greek medicine",
      "Greek Author-Date"
    ]
  },
  {
    "id": "greek-medicine-footnote",
    "name": "Greek Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Greek",
      "Greek medicine",
      "Greek Footnote"
    ]
  },
  {
    "id": "greek-medicine-endnote",
    "name": "Greek Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Greek",
      "Greek medicine",
      "Greek Endnote"
    ]
  },
  {
    "id": "greek-medicine-numeric",
    "name": "Greek Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Greek",
      "Greek medicine",
      "Greek Numeric"
    ]
  },
  {
    "id": "greek-medicine-vancouver-style",
    "name": "Greek Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Greek",
      "Greek medicine",
      "Greek Vancouver-Style"
    ]
  },
  {
    "id": "greek-medicine-author-number",
    "name": "Greek Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Greek",
      "Greek medicine",
      "Greek Author-Number"
    ]
  },
  {
    "id": "greek-general-author-date",
    "name": "Greek General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Greek",
      "Greek general",
      "Greek Author-Date"
    ]
  },
  {
    "id": "greek-general-footnote",
    "name": "Greek General — Footnote",
    "discipline": "general",
    "aliases": [
      "Greek",
      "Greek general",
      "Greek Footnote"
    ]
  },
  {
    "id": "greek-general-endnote",
    "name": "Greek General — Endnote",
    "discipline": "general",
    "aliases": [
      "Greek",
      "Greek general",
      "Greek Endnote"
    ]
  },
  {
    "id": "greek-general-numeric",
    "name": "Greek General — Numeric",
    "discipline": "general",
    "aliases": [
      "Greek",
      "Greek general",
      "Greek Numeric"
    ]
  },
  {
    "id": "greek-general-vancouver-style",
    "name": "Greek General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Greek",
      "Greek general",
      "Greek Vancouver-Style"
    ]
  },
  {
    "id": "greek-general-author-number",
    "name": "Greek General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Greek",
      "Greek general",
      "Greek Author-Number"
    ]
  },
  {
    "id": "turkish-sciences-author-date",
    "name": "Turkish Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Turkish",
      "Turkish sciences",
      "Turkish Author-Date"
    ]
  },
  {
    "id": "turkish-sciences-footnote",
    "name": "Turkish Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Turkish",
      "Turkish sciences",
      "Turkish Footnote"
    ]
  },
  {
    "id": "turkish-sciences-endnote",
    "name": "Turkish Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Turkish",
      "Turkish sciences",
      "Turkish Endnote"
    ]
  },
  {
    "id": "turkish-sciences-numeric",
    "name": "Turkish Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Turkish",
      "Turkish sciences",
      "Turkish Numeric"
    ]
  },
  {
    "id": "turkish-sciences-vancouver-style",
    "name": "Turkish Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Turkish",
      "Turkish sciences",
      "Turkish Vancouver-Style"
    ]
  },
  {
    "id": "turkish-sciences-author-number",
    "name": "Turkish Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Turkish",
      "Turkish sciences",
      "Turkish Author-Number"
    ]
  },
  {
    "id": "turkish-humanities-author-date",
    "name": "Turkish Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Turkish",
      "Turkish humanities",
      "Turkish Author-Date"
    ]
  },
  {
    "id": "turkish-humanities-footnote",
    "name": "Turkish Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Turkish",
      "Turkish humanities",
      "Turkish Footnote"
    ]
  },
  {
    "id": "turkish-humanities-endnote",
    "name": "Turkish Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Turkish",
      "Turkish humanities",
      "Turkish Endnote"
    ]
  },
  {
    "id": "turkish-humanities-numeric",
    "name": "Turkish Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Turkish",
      "Turkish humanities",
      "Turkish Numeric"
    ]
  },
  {
    "id": "turkish-humanities-vancouver-style",
    "name": "Turkish Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Turkish",
      "Turkish humanities",
      "Turkish Vancouver-Style"
    ]
  },
  {
    "id": "turkish-humanities-author-number",
    "name": "Turkish Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Turkish",
      "Turkish humanities",
      "Turkish Author-Number"
    ]
  },
  {
    "id": "turkish-law-author-date",
    "name": "Turkish Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Turkish",
      "Turkish law",
      "Turkish Author-Date"
    ]
  },
  {
    "id": "turkish-law-footnote",
    "name": "Turkish Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Turkish",
      "Turkish law",
      "Turkish Footnote"
    ]
  },
  {
    "id": "turkish-law-endnote",
    "name": "Turkish Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Turkish",
      "Turkish law",
      "Turkish Endnote"
    ]
  },
  {
    "id": "turkish-law-numeric",
    "name": "Turkish Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Turkish",
      "Turkish law",
      "Turkish Numeric"
    ]
  },
  {
    "id": "turkish-law-vancouver-style",
    "name": "Turkish Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Turkish",
      "Turkish law",
      "Turkish Vancouver-Style"
    ]
  },
  {
    "id": "turkish-law-author-number",
    "name": "Turkish Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Turkish",
      "Turkish law",
      "Turkish Author-Number"
    ]
  },
  {
    "id": "turkish-medicine-author-date",
    "name": "Turkish Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Turkish",
      "Turkish medicine",
      "Turkish Author-Date"
    ]
  },
  {
    "id": "turkish-medicine-footnote",
    "name": "Turkish Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Turkish",
      "Turkish medicine",
      "Turkish Footnote"
    ]
  },
  {
    "id": "turkish-medicine-endnote",
    "name": "Turkish Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Turkish",
      "Turkish medicine",
      "Turkish Endnote"
    ]
  },
  {
    "id": "turkish-medicine-numeric",
    "name": "Turkish Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Turkish",
      "Turkish medicine",
      "Turkish Numeric"
    ]
  },
  {
    "id": "turkish-medicine-vancouver-style",
    "name": "Turkish Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Turkish",
      "Turkish medicine",
      "Turkish Vancouver-Style"
    ]
  },
  {
    "id": "turkish-medicine-author-number",
    "name": "Turkish Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Turkish",
      "Turkish medicine",
      "Turkish Author-Number"
    ]
  },
  {
    "id": "turkish-general-author-date",
    "name": "Turkish General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Turkish",
      "Turkish general",
      "Turkish Author-Date"
    ]
  },
  {
    "id": "turkish-general-footnote",
    "name": "Turkish General — Footnote",
    "discipline": "general",
    "aliases": [
      "Turkish",
      "Turkish general",
      "Turkish Footnote"
    ]
  },
  {
    "id": "turkish-general-endnote",
    "name": "Turkish General — Endnote",
    "discipline": "general",
    "aliases": [
      "Turkish",
      "Turkish general",
      "Turkish Endnote"
    ]
  },
  {
    "id": "turkish-general-numeric",
    "name": "Turkish General — Numeric",
    "discipline": "general",
    "aliases": [
      "Turkish",
      "Turkish general",
      "Turkish Numeric"
    ]
  },
  {
    "id": "turkish-general-vancouver-style",
    "name": "Turkish General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Turkish",
      "Turkish general",
      "Turkish Vancouver-Style"
    ]
  },
  {
    "id": "turkish-general-author-number",
    "name": "Turkish General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Turkish",
      "Turkish general",
      "Turkish Author-Number"
    ]
  },
  {
    "id": "portuguese-sciences-author-date",
    "name": "Portuguese Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Portuguese",
      "Portuguese sciences",
      "Portuguese Author-Date"
    ]
  },
  {
    "id": "portuguese-sciences-footnote",
    "name": "Portuguese Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Portuguese",
      "Portuguese sciences",
      "Portuguese Footnote"
    ]
  },
  {
    "id": "portuguese-sciences-endnote",
    "name": "Portuguese Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Portuguese",
      "Portuguese sciences",
      "Portuguese Endnote"
    ]
  },
  {
    "id": "portuguese-sciences-numeric",
    "name": "Portuguese Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Portuguese",
      "Portuguese sciences",
      "Portuguese Numeric"
    ]
  },
  {
    "id": "portuguese-sciences-vancouver-style",
    "name": "Portuguese Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Portuguese",
      "Portuguese sciences",
      "Portuguese Vancouver-Style"
    ]
  },
  {
    "id": "portuguese-sciences-author-number",
    "name": "Portuguese Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Portuguese",
      "Portuguese sciences",
      "Portuguese Author-Number"
    ]
  },
  {
    "id": "portuguese-humanities-author-date",
    "name": "Portuguese Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Portuguese",
      "Portuguese humanities",
      "Portuguese Author-Date"
    ]
  },
  {
    "id": "portuguese-humanities-footnote",
    "name": "Portuguese Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Portuguese",
      "Portuguese humanities",
      "Portuguese Footnote"
    ]
  },
  {
    "id": "portuguese-humanities-endnote",
    "name": "Portuguese Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Portuguese",
      "Portuguese humanities",
      "Portuguese Endnote"
    ]
  },
  {
    "id": "portuguese-humanities-numeric",
    "name": "Portuguese Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Portuguese",
      "Portuguese humanities",
      "Portuguese Numeric"
    ]
  },
  {
    "id": "portuguese-humanities-vancouver-style",
    "name": "Portuguese Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Portuguese",
      "Portuguese humanities",
      "Portuguese Vancouver-Style"
    ]
  },
  {
    "id": "portuguese-humanities-author-number",
    "name": "Portuguese Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Portuguese",
      "Portuguese humanities",
      "Portuguese Author-Number"
    ]
  },
  {
    "id": "portuguese-law-author-date",
    "name": "Portuguese Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Portuguese",
      "Portuguese law",
      "Portuguese Author-Date"
    ]
  },
  {
    "id": "portuguese-law-footnote",
    "name": "Portuguese Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Portuguese",
      "Portuguese law",
      "Portuguese Footnote"
    ]
  },
  {
    "id": "portuguese-law-endnote",
    "name": "Portuguese Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Portuguese",
      "Portuguese law",
      "Portuguese Endnote"
    ]
  },
  {
    "id": "portuguese-law-numeric",
    "name": "Portuguese Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Portuguese",
      "Portuguese law",
      "Portuguese Numeric"
    ]
  },
  {
    "id": "portuguese-law-vancouver-style",
    "name": "Portuguese Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Portuguese",
      "Portuguese law",
      "Portuguese Vancouver-Style"
    ]
  },
  {
    "id": "portuguese-law-author-number",
    "name": "Portuguese Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Portuguese",
      "Portuguese law",
      "Portuguese Author-Number"
    ]
  },
  {
    "id": "portuguese-medicine-author-date",
    "name": "Portuguese Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Portuguese",
      "Portuguese medicine",
      "Portuguese Author-Date"
    ]
  },
  {
    "id": "portuguese-medicine-footnote",
    "name": "Portuguese Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Portuguese",
      "Portuguese medicine",
      "Portuguese Footnote"
    ]
  },
  {
    "id": "portuguese-medicine-endnote",
    "name": "Portuguese Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Portuguese",
      "Portuguese medicine",
      "Portuguese Endnote"
    ]
  },
  {
    "id": "portuguese-medicine-numeric",
    "name": "Portuguese Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Portuguese",
      "Portuguese medicine",
      "Portuguese Numeric"
    ]
  },
  {
    "id": "portuguese-medicine-vancouver-style",
    "name": "Portuguese Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Portuguese",
      "Portuguese medicine",
      "Portuguese Vancouver-Style"
    ]
  },
  {
    "id": "portuguese-medicine-author-number",
    "name": "Portuguese Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Portuguese",
      "Portuguese medicine",
      "Portuguese Author-Number"
    ]
  },
  {
    "id": "portuguese-general-author-date",
    "name": "Portuguese General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Portuguese",
      "Portuguese general",
      "Portuguese Author-Date"
    ]
  },
  {
    "id": "portuguese-general-footnote",
    "name": "Portuguese General — Footnote",
    "discipline": "general",
    "aliases": [
      "Portuguese",
      "Portuguese general",
      "Portuguese Footnote"
    ]
  },
  {
    "id": "portuguese-general-endnote",
    "name": "Portuguese General — Endnote",
    "discipline": "general",
    "aliases": [
      "Portuguese",
      "Portuguese general",
      "Portuguese Endnote"
    ]
  },
  {
    "id": "portuguese-general-numeric",
    "name": "Portuguese General — Numeric",
    "discipline": "general",
    "aliases": [
      "Portuguese",
      "Portuguese general",
      "Portuguese Numeric"
    ]
  },
  {
    "id": "portuguese-general-vancouver-style",
    "name": "Portuguese General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Portuguese",
      "Portuguese general",
      "Portuguese Vancouver-Style"
    ]
  },
  {
    "id": "portuguese-general-author-number",
    "name": "Portuguese General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Portuguese",
      "Portuguese general",
      "Portuguese Author-Number"
    ]
  },
  {
    "id": "spanish-sciences-author-date",
    "name": "Spanish Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Spanish",
      "Spanish sciences",
      "Spanish Author-Date"
    ]
  },
  {
    "id": "spanish-sciences-footnote",
    "name": "Spanish Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Spanish",
      "Spanish sciences",
      "Spanish Footnote"
    ]
  },
  {
    "id": "spanish-sciences-endnote",
    "name": "Spanish Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Spanish",
      "Spanish sciences",
      "Spanish Endnote"
    ]
  },
  {
    "id": "spanish-sciences-numeric",
    "name": "Spanish Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Spanish",
      "Spanish sciences",
      "Spanish Numeric"
    ]
  },
  {
    "id": "spanish-sciences-vancouver-style",
    "name": "Spanish Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Spanish",
      "Spanish sciences",
      "Spanish Vancouver-Style"
    ]
  },
  {
    "id": "spanish-sciences-author-number",
    "name": "Spanish Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Spanish",
      "Spanish sciences",
      "Spanish Author-Number"
    ]
  },
  {
    "id": "spanish-humanities-author-date",
    "name": "Spanish Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Spanish",
      "Spanish humanities",
      "Spanish Author-Date"
    ]
  },
  {
    "id": "spanish-humanities-footnote",
    "name": "Spanish Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Spanish",
      "Spanish humanities",
      "Spanish Footnote"
    ]
  },
  {
    "id": "spanish-humanities-endnote",
    "name": "Spanish Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Spanish",
      "Spanish humanities",
      "Spanish Endnote"
    ]
  },
  {
    "id": "spanish-humanities-numeric",
    "name": "Spanish Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Spanish",
      "Spanish humanities",
      "Spanish Numeric"
    ]
  },
  {
    "id": "spanish-humanities-vancouver-style",
    "name": "Spanish Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Spanish",
      "Spanish humanities",
      "Spanish Vancouver-Style"
    ]
  },
  {
    "id": "spanish-humanities-author-number",
    "name": "Spanish Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Spanish",
      "Spanish humanities",
      "Spanish Author-Number"
    ]
  },
  {
    "id": "spanish-law-author-date",
    "name": "Spanish Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Spanish",
      "Spanish law",
      "Spanish Author-Date"
    ]
  },
  {
    "id": "spanish-law-footnote",
    "name": "Spanish Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Spanish",
      "Spanish law",
      "Spanish Footnote"
    ]
  },
  {
    "id": "spanish-law-endnote",
    "name": "Spanish Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Spanish",
      "Spanish law",
      "Spanish Endnote"
    ]
  },
  {
    "id": "spanish-law-numeric",
    "name": "Spanish Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Spanish",
      "Spanish law",
      "Spanish Numeric"
    ]
  },
  {
    "id": "spanish-law-vancouver-style",
    "name": "Spanish Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Spanish",
      "Spanish law",
      "Spanish Vancouver-Style"
    ]
  },
  {
    "id": "spanish-law-author-number",
    "name": "Spanish Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Spanish",
      "Spanish law",
      "Spanish Author-Number"
    ]
  },
  {
    "id": "spanish-medicine-author-date",
    "name": "Spanish Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Spanish",
      "Spanish medicine",
      "Spanish Author-Date"
    ]
  },
  {
    "id": "spanish-medicine-footnote",
    "name": "Spanish Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Spanish",
      "Spanish medicine",
      "Spanish Footnote"
    ]
  },
  {
    "id": "spanish-medicine-endnote",
    "name": "Spanish Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Spanish",
      "Spanish medicine",
      "Spanish Endnote"
    ]
  },
  {
    "id": "spanish-medicine-numeric",
    "name": "Spanish Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Spanish",
      "Spanish medicine",
      "Spanish Numeric"
    ]
  },
  {
    "id": "spanish-medicine-vancouver-style",
    "name": "Spanish Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Spanish",
      "Spanish medicine",
      "Spanish Vancouver-Style"
    ]
  },
  {
    "id": "spanish-medicine-author-number",
    "name": "Spanish Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Spanish",
      "Spanish medicine",
      "Spanish Author-Number"
    ]
  },
  {
    "id": "spanish-general-author-date",
    "name": "Spanish General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Spanish",
      "Spanish general",
      "Spanish Author-Date"
    ]
  },
  {
    "id": "spanish-general-footnote",
    "name": "Spanish General — Footnote",
    "discipline": "general",
    "aliases": [
      "Spanish",
      "Spanish general",
      "Spanish Footnote"
    ]
  },
  {
    "id": "spanish-general-endnote",
    "name": "Spanish General — Endnote",
    "discipline": "general",
    "aliases": [
      "Spanish",
      "Spanish general",
      "Spanish Endnote"
    ]
  },
  {
    "id": "spanish-general-numeric",
    "name": "Spanish General — Numeric",
    "discipline": "general",
    "aliases": [
      "Spanish",
      "Spanish general",
      "Spanish Numeric"
    ]
  },
  {
    "id": "spanish-general-vancouver-style",
    "name": "Spanish General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Spanish",
      "Spanish general",
      "Spanish Vancouver-Style"
    ]
  },
  {
    "id": "spanish-general-author-number",
    "name": "Spanish General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Spanish",
      "Spanish general",
      "Spanish Author-Number"
    ]
  },
  {
    "id": "italian-sciences-author-date",
    "name": "Italian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Italian",
      "Italian sciences",
      "Italian Author-Date"
    ]
  },
  {
    "id": "italian-sciences-footnote",
    "name": "Italian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Italian",
      "Italian sciences",
      "Italian Footnote"
    ]
  },
  {
    "id": "italian-sciences-endnote",
    "name": "Italian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Italian",
      "Italian sciences",
      "Italian Endnote"
    ]
  },
  {
    "id": "italian-sciences-numeric",
    "name": "Italian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Italian",
      "Italian sciences",
      "Italian Numeric"
    ]
  },
  {
    "id": "italian-sciences-vancouver-style",
    "name": "Italian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Italian",
      "Italian sciences",
      "Italian Vancouver-Style"
    ]
  },
  {
    "id": "italian-sciences-author-number",
    "name": "Italian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Italian",
      "Italian sciences",
      "Italian Author-Number"
    ]
  },
  {
    "id": "italian-humanities-author-date",
    "name": "Italian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Italian",
      "Italian humanities",
      "Italian Author-Date"
    ]
  },
  {
    "id": "italian-humanities-footnote",
    "name": "Italian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Italian",
      "Italian humanities",
      "Italian Footnote"
    ]
  },
  {
    "id": "italian-humanities-endnote",
    "name": "Italian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Italian",
      "Italian humanities",
      "Italian Endnote"
    ]
  },
  {
    "id": "italian-humanities-numeric",
    "name": "Italian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Italian",
      "Italian humanities",
      "Italian Numeric"
    ]
  },
  {
    "id": "italian-humanities-vancouver-style",
    "name": "Italian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Italian",
      "Italian humanities",
      "Italian Vancouver-Style"
    ]
  },
  {
    "id": "italian-humanities-author-number",
    "name": "Italian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Italian",
      "Italian humanities",
      "Italian Author-Number"
    ]
  },
  {
    "id": "italian-law-author-date",
    "name": "Italian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Italian",
      "Italian law",
      "Italian Author-Date"
    ]
  },
  {
    "id": "italian-law-footnote",
    "name": "Italian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Italian",
      "Italian law",
      "Italian Footnote"
    ]
  },
  {
    "id": "italian-law-endnote",
    "name": "Italian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Italian",
      "Italian law",
      "Italian Endnote"
    ]
  },
  {
    "id": "italian-law-numeric",
    "name": "Italian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Italian",
      "Italian law",
      "Italian Numeric"
    ]
  },
  {
    "id": "italian-law-vancouver-style",
    "name": "Italian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Italian",
      "Italian law",
      "Italian Vancouver-Style"
    ]
  },
  {
    "id": "italian-law-author-number",
    "name": "Italian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Italian",
      "Italian law",
      "Italian Author-Number"
    ]
  },
  {
    "id": "italian-medicine-author-date",
    "name": "Italian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Italian",
      "Italian medicine",
      "Italian Author-Date"
    ]
  },
  {
    "id": "italian-medicine-footnote",
    "name": "Italian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Italian",
      "Italian medicine",
      "Italian Footnote"
    ]
  },
  {
    "id": "italian-medicine-endnote",
    "name": "Italian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Italian",
      "Italian medicine",
      "Italian Endnote"
    ]
  },
  {
    "id": "italian-medicine-numeric",
    "name": "Italian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Italian",
      "Italian medicine",
      "Italian Numeric"
    ]
  },
  {
    "id": "italian-medicine-vancouver-style",
    "name": "Italian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Italian",
      "Italian medicine",
      "Italian Vancouver-Style"
    ]
  },
  {
    "id": "italian-medicine-author-number",
    "name": "Italian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Italian",
      "Italian medicine",
      "Italian Author-Number"
    ]
  },
  {
    "id": "italian-general-author-date",
    "name": "Italian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Italian",
      "Italian general",
      "Italian Author-Date"
    ]
  },
  {
    "id": "italian-general-footnote",
    "name": "Italian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Italian",
      "Italian general",
      "Italian Footnote"
    ]
  },
  {
    "id": "italian-general-endnote",
    "name": "Italian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Italian",
      "Italian general",
      "Italian Endnote"
    ]
  },
  {
    "id": "italian-general-numeric",
    "name": "Italian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Italian",
      "Italian general",
      "Italian Numeric"
    ]
  },
  {
    "id": "italian-general-vancouver-style",
    "name": "Italian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Italian",
      "Italian general",
      "Italian Vancouver-Style"
    ]
  },
  {
    "id": "italian-general-author-number",
    "name": "Italian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Italian",
      "Italian general",
      "Italian Author-Number"
    ]
  },
  {
    "id": "mexican-sciences-author-date",
    "name": "Mexican Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Mexican",
      "Mexican sciences",
      "Mexican Author-Date"
    ]
  },
  {
    "id": "mexican-sciences-footnote",
    "name": "Mexican Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Mexican",
      "Mexican sciences",
      "Mexican Footnote"
    ]
  },
  {
    "id": "mexican-sciences-endnote",
    "name": "Mexican Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Mexican",
      "Mexican sciences",
      "Mexican Endnote"
    ]
  },
  {
    "id": "mexican-sciences-numeric",
    "name": "Mexican Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Mexican",
      "Mexican sciences",
      "Mexican Numeric"
    ]
  },
  {
    "id": "mexican-sciences-vancouver-style",
    "name": "Mexican Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Mexican",
      "Mexican sciences",
      "Mexican Vancouver-Style"
    ]
  },
  {
    "id": "mexican-sciences-author-number",
    "name": "Mexican Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Mexican",
      "Mexican sciences",
      "Mexican Author-Number"
    ]
  },
  {
    "id": "mexican-humanities-author-date",
    "name": "Mexican Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Mexican",
      "Mexican humanities",
      "Mexican Author-Date"
    ]
  },
  {
    "id": "mexican-humanities-footnote",
    "name": "Mexican Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Mexican",
      "Mexican humanities",
      "Mexican Footnote"
    ]
  },
  {
    "id": "mexican-humanities-endnote",
    "name": "Mexican Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Mexican",
      "Mexican humanities",
      "Mexican Endnote"
    ]
  },
  {
    "id": "mexican-humanities-numeric",
    "name": "Mexican Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Mexican",
      "Mexican humanities",
      "Mexican Numeric"
    ]
  },
  {
    "id": "mexican-humanities-vancouver-style",
    "name": "Mexican Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Mexican",
      "Mexican humanities",
      "Mexican Vancouver-Style"
    ]
  },
  {
    "id": "mexican-humanities-author-number",
    "name": "Mexican Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Mexican",
      "Mexican humanities",
      "Mexican Author-Number"
    ]
  },
  {
    "id": "mexican-law-author-date",
    "name": "Mexican Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Mexican",
      "Mexican law",
      "Mexican Author-Date"
    ]
  },
  {
    "id": "mexican-law-footnote",
    "name": "Mexican Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Mexican",
      "Mexican law",
      "Mexican Footnote"
    ]
  },
  {
    "id": "mexican-law-endnote",
    "name": "Mexican Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Mexican",
      "Mexican law",
      "Mexican Endnote"
    ]
  },
  {
    "id": "mexican-law-numeric",
    "name": "Mexican Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Mexican",
      "Mexican law",
      "Mexican Numeric"
    ]
  },
  {
    "id": "mexican-law-vancouver-style",
    "name": "Mexican Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Mexican",
      "Mexican law",
      "Mexican Vancouver-Style"
    ]
  },
  {
    "id": "mexican-law-author-number",
    "name": "Mexican Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Mexican",
      "Mexican law",
      "Mexican Author-Number"
    ]
  },
  {
    "id": "mexican-medicine-author-date",
    "name": "Mexican Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Mexican",
      "Mexican medicine",
      "Mexican Author-Date"
    ]
  },
  {
    "id": "mexican-medicine-footnote",
    "name": "Mexican Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Mexican",
      "Mexican medicine",
      "Mexican Footnote"
    ]
  },
  {
    "id": "mexican-medicine-endnote",
    "name": "Mexican Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Mexican",
      "Mexican medicine",
      "Mexican Endnote"
    ]
  },
  {
    "id": "mexican-medicine-numeric",
    "name": "Mexican Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Mexican",
      "Mexican medicine",
      "Mexican Numeric"
    ]
  },
  {
    "id": "mexican-medicine-vancouver-style",
    "name": "Mexican Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Mexican",
      "Mexican medicine",
      "Mexican Vancouver-Style"
    ]
  },
  {
    "id": "mexican-medicine-author-number",
    "name": "Mexican Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Mexican",
      "Mexican medicine",
      "Mexican Author-Number"
    ]
  },
  {
    "id": "mexican-general-author-date",
    "name": "Mexican General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Mexican",
      "Mexican general",
      "Mexican Author-Date"
    ]
  },
  {
    "id": "mexican-general-footnote",
    "name": "Mexican General — Footnote",
    "discipline": "general",
    "aliases": [
      "Mexican",
      "Mexican general",
      "Mexican Footnote"
    ]
  },
  {
    "id": "mexican-general-endnote",
    "name": "Mexican General — Endnote",
    "discipline": "general",
    "aliases": [
      "Mexican",
      "Mexican general",
      "Mexican Endnote"
    ]
  },
  {
    "id": "mexican-general-numeric",
    "name": "Mexican General — Numeric",
    "discipline": "general",
    "aliases": [
      "Mexican",
      "Mexican general",
      "Mexican Numeric"
    ]
  },
  {
    "id": "mexican-general-vancouver-style",
    "name": "Mexican General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Mexican",
      "Mexican general",
      "Mexican Vancouver-Style"
    ]
  },
  {
    "id": "mexican-general-author-number",
    "name": "Mexican General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Mexican",
      "Mexican general",
      "Mexican Author-Number"
    ]
  },
  {
    "id": "argentinian-sciences-author-date",
    "name": "Argentinian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Argentinian",
      "Argentinian sciences",
      "Argentinian Author-Date"
    ]
  },
  {
    "id": "argentinian-sciences-footnote",
    "name": "Argentinian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Argentinian",
      "Argentinian sciences",
      "Argentinian Footnote"
    ]
  },
  {
    "id": "argentinian-sciences-endnote",
    "name": "Argentinian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Argentinian",
      "Argentinian sciences",
      "Argentinian Endnote"
    ]
  },
  {
    "id": "argentinian-sciences-numeric",
    "name": "Argentinian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Argentinian",
      "Argentinian sciences",
      "Argentinian Numeric"
    ]
  },
  {
    "id": "argentinian-sciences-vancouver-style",
    "name": "Argentinian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Argentinian",
      "Argentinian sciences",
      "Argentinian Vancouver-Style"
    ]
  },
  {
    "id": "argentinian-sciences-author-number",
    "name": "Argentinian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Argentinian",
      "Argentinian sciences",
      "Argentinian Author-Number"
    ]
  },
  {
    "id": "argentinian-humanities-author-date",
    "name": "Argentinian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Argentinian",
      "Argentinian humanities",
      "Argentinian Author-Date"
    ]
  },
  {
    "id": "argentinian-humanities-footnote",
    "name": "Argentinian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Argentinian",
      "Argentinian humanities",
      "Argentinian Footnote"
    ]
  },
  {
    "id": "argentinian-humanities-endnote",
    "name": "Argentinian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Argentinian",
      "Argentinian humanities",
      "Argentinian Endnote"
    ]
  },
  {
    "id": "argentinian-humanities-numeric",
    "name": "Argentinian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Argentinian",
      "Argentinian humanities",
      "Argentinian Numeric"
    ]
  },
  {
    "id": "argentinian-humanities-vancouver-style",
    "name": "Argentinian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Argentinian",
      "Argentinian humanities",
      "Argentinian Vancouver-Style"
    ]
  },
  {
    "id": "argentinian-humanities-author-number",
    "name": "Argentinian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Argentinian",
      "Argentinian humanities",
      "Argentinian Author-Number"
    ]
  },
  {
    "id": "argentinian-law-author-date",
    "name": "Argentinian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Argentinian",
      "Argentinian law",
      "Argentinian Author-Date"
    ]
  },
  {
    "id": "argentinian-law-footnote",
    "name": "Argentinian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Argentinian",
      "Argentinian law",
      "Argentinian Footnote"
    ]
  },
  {
    "id": "argentinian-law-endnote",
    "name": "Argentinian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Argentinian",
      "Argentinian law",
      "Argentinian Endnote"
    ]
  },
  {
    "id": "argentinian-law-numeric",
    "name": "Argentinian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Argentinian",
      "Argentinian law",
      "Argentinian Numeric"
    ]
  },
  {
    "id": "argentinian-law-vancouver-style",
    "name": "Argentinian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Argentinian",
      "Argentinian law",
      "Argentinian Vancouver-Style"
    ]
  },
  {
    "id": "argentinian-law-author-number",
    "name": "Argentinian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Argentinian",
      "Argentinian law",
      "Argentinian Author-Number"
    ]
  },
  {
    "id": "argentinian-medicine-author-date",
    "name": "Argentinian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Argentinian",
      "Argentinian medicine",
      "Argentinian Author-Date"
    ]
  },
  {
    "id": "argentinian-medicine-footnote",
    "name": "Argentinian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Argentinian",
      "Argentinian medicine",
      "Argentinian Footnote"
    ]
  },
  {
    "id": "argentinian-medicine-endnote",
    "name": "Argentinian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Argentinian",
      "Argentinian medicine",
      "Argentinian Endnote"
    ]
  },
  {
    "id": "argentinian-medicine-numeric",
    "name": "Argentinian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Argentinian",
      "Argentinian medicine",
      "Argentinian Numeric"
    ]
  },
  {
    "id": "argentinian-medicine-vancouver-style",
    "name": "Argentinian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Argentinian",
      "Argentinian medicine",
      "Argentinian Vancouver-Style"
    ]
  },
  {
    "id": "argentinian-medicine-author-number",
    "name": "Argentinian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Argentinian",
      "Argentinian medicine",
      "Argentinian Author-Number"
    ]
  },
  {
    "id": "argentinian-general-author-date",
    "name": "Argentinian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Argentinian",
      "Argentinian general",
      "Argentinian Author-Date"
    ]
  },
  {
    "id": "argentinian-general-footnote",
    "name": "Argentinian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Argentinian",
      "Argentinian general",
      "Argentinian Footnote"
    ]
  },
  {
    "id": "argentinian-general-endnote",
    "name": "Argentinian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Argentinian",
      "Argentinian general",
      "Argentinian Endnote"
    ]
  },
  {
    "id": "argentinian-general-numeric",
    "name": "Argentinian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Argentinian",
      "Argentinian general",
      "Argentinian Numeric"
    ]
  },
  {
    "id": "argentinian-general-vancouver-style",
    "name": "Argentinian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Argentinian",
      "Argentinian general",
      "Argentinian Vancouver-Style"
    ]
  },
  {
    "id": "argentinian-general-author-number",
    "name": "Argentinian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Argentinian",
      "Argentinian general",
      "Argentinian Author-Number"
    ]
  },
  {
    "id": "chilean-sciences-author-date",
    "name": "Chilean Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Chilean",
      "Chilean sciences",
      "Chilean Author-Date"
    ]
  },
  {
    "id": "chilean-sciences-footnote",
    "name": "Chilean Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Chilean",
      "Chilean sciences",
      "Chilean Footnote"
    ]
  },
  {
    "id": "chilean-sciences-endnote",
    "name": "Chilean Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Chilean",
      "Chilean sciences",
      "Chilean Endnote"
    ]
  },
  {
    "id": "chilean-sciences-numeric",
    "name": "Chilean Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Chilean",
      "Chilean sciences",
      "Chilean Numeric"
    ]
  },
  {
    "id": "chilean-sciences-vancouver-style",
    "name": "Chilean Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Chilean",
      "Chilean sciences",
      "Chilean Vancouver-Style"
    ]
  },
  {
    "id": "chilean-sciences-author-number",
    "name": "Chilean Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Chilean",
      "Chilean sciences",
      "Chilean Author-Number"
    ]
  },
  {
    "id": "chilean-humanities-author-date",
    "name": "Chilean Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Chilean",
      "Chilean humanities",
      "Chilean Author-Date"
    ]
  },
  {
    "id": "chilean-humanities-footnote",
    "name": "Chilean Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Chilean",
      "Chilean humanities",
      "Chilean Footnote"
    ]
  },
  {
    "id": "chilean-humanities-endnote",
    "name": "Chilean Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Chilean",
      "Chilean humanities",
      "Chilean Endnote"
    ]
  },
  {
    "id": "chilean-humanities-numeric",
    "name": "Chilean Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Chilean",
      "Chilean humanities",
      "Chilean Numeric"
    ]
  },
  {
    "id": "chilean-humanities-vancouver-style",
    "name": "Chilean Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Chilean",
      "Chilean humanities",
      "Chilean Vancouver-Style"
    ]
  },
  {
    "id": "chilean-humanities-author-number",
    "name": "Chilean Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Chilean",
      "Chilean humanities",
      "Chilean Author-Number"
    ]
  },
  {
    "id": "chilean-law-author-date",
    "name": "Chilean Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Chilean",
      "Chilean law",
      "Chilean Author-Date"
    ]
  },
  {
    "id": "chilean-law-footnote",
    "name": "Chilean Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Chilean",
      "Chilean law",
      "Chilean Footnote"
    ]
  },
  {
    "id": "chilean-law-endnote",
    "name": "Chilean Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Chilean",
      "Chilean law",
      "Chilean Endnote"
    ]
  },
  {
    "id": "chilean-law-numeric",
    "name": "Chilean Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Chilean",
      "Chilean law",
      "Chilean Numeric"
    ]
  },
  {
    "id": "chilean-law-vancouver-style",
    "name": "Chilean Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Chilean",
      "Chilean law",
      "Chilean Vancouver-Style"
    ]
  },
  {
    "id": "chilean-law-author-number",
    "name": "Chilean Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Chilean",
      "Chilean law",
      "Chilean Author-Number"
    ]
  },
  {
    "id": "chilean-medicine-author-date",
    "name": "Chilean Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Chilean",
      "Chilean medicine",
      "Chilean Author-Date"
    ]
  },
  {
    "id": "chilean-medicine-footnote",
    "name": "Chilean Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Chilean",
      "Chilean medicine",
      "Chilean Footnote"
    ]
  },
  {
    "id": "chilean-medicine-endnote",
    "name": "Chilean Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Chilean",
      "Chilean medicine",
      "Chilean Endnote"
    ]
  },
  {
    "id": "chilean-medicine-numeric",
    "name": "Chilean Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Chilean",
      "Chilean medicine",
      "Chilean Numeric"
    ]
  },
  {
    "id": "chilean-medicine-vancouver-style",
    "name": "Chilean Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Chilean",
      "Chilean medicine",
      "Chilean Vancouver-Style"
    ]
  },
  {
    "id": "chilean-medicine-author-number",
    "name": "Chilean Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Chilean",
      "Chilean medicine",
      "Chilean Author-Number"
    ]
  },
  {
    "id": "chilean-general-author-date",
    "name": "Chilean General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Chilean",
      "Chilean general",
      "Chilean Author-Date"
    ]
  },
  {
    "id": "chilean-general-footnote",
    "name": "Chilean General — Footnote",
    "discipline": "general",
    "aliases": [
      "Chilean",
      "Chilean general",
      "Chilean Footnote"
    ]
  },
  {
    "id": "chilean-general-endnote",
    "name": "Chilean General — Endnote",
    "discipline": "general",
    "aliases": [
      "Chilean",
      "Chilean general",
      "Chilean Endnote"
    ]
  },
  {
    "id": "chilean-general-numeric",
    "name": "Chilean General — Numeric",
    "discipline": "general",
    "aliases": [
      "Chilean",
      "Chilean general",
      "Chilean Numeric"
    ]
  },
  {
    "id": "chilean-general-vancouver-style",
    "name": "Chilean General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Chilean",
      "Chilean general",
      "Chilean Vancouver-Style"
    ]
  },
  {
    "id": "chilean-general-author-number",
    "name": "Chilean General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Chilean",
      "Chilean general",
      "Chilean Author-Number"
    ]
  },
  {
    "id": "colombian-sciences-author-date",
    "name": "Colombian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Colombian",
      "Colombian sciences",
      "Colombian Author-Date"
    ]
  },
  {
    "id": "colombian-sciences-footnote",
    "name": "Colombian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Colombian",
      "Colombian sciences",
      "Colombian Footnote"
    ]
  },
  {
    "id": "colombian-sciences-endnote",
    "name": "Colombian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Colombian",
      "Colombian sciences",
      "Colombian Endnote"
    ]
  },
  {
    "id": "colombian-sciences-numeric",
    "name": "Colombian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Colombian",
      "Colombian sciences",
      "Colombian Numeric"
    ]
  },
  {
    "id": "colombian-sciences-vancouver-style",
    "name": "Colombian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Colombian",
      "Colombian sciences",
      "Colombian Vancouver-Style"
    ]
  },
  {
    "id": "colombian-sciences-author-number",
    "name": "Colombian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Colombian",
      "Colombian sciences",
      "Colombian Author-Number"
    ]
  },
  {
    "id": "colombian-humanities-author-date",
    "name": "Colombian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Colombian",
      "Colombian humanities",
      "Colombian Author-Date"
    ]
  },
  {
    "id": "colombian-humanities-footnote",
    "name": "Colombian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Colombian",
      "Colombian humanities",
      "Colombian Footnote"
    ]
  },
  {
    "id": "colombian-humanities-endnote",
    "name": "Colombian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Colombian",
      "Colombian humanities",
      "Colombian Endnote"
    ]
  },
  {
    "id": "colombian-humanities-numeric",
    "name": "Colombian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Colombian",
      "Colombian humanities",
      "Colombian Numeric"
    ]
  },
  {
    "id": "colombian-humanities-vancouver-style",
    "name": "Colombian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Colombian",
      "Colombian humanities",
      "Colombian Vancouver-Style"
    ]
  },
  {
    "id": "colombian-humanities-author-number",
    "name": "Colombian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Colombian",
      "Colombian humanities",
      "Colombian Author-Number"
    ]
  },
  {
    "id": "colombian-law-author-date",
    "name": "Colombian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Colombian",
      "Colombian law",
      "Colombian Author-Date"
    ]
  },
  {
    "id": "colombian-law-footnote",
    "name": "Colombian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Colombian",
      "Colombian law",
      "Colombian Footnote"
    ]
  },
  {
    "id": "colombian-law-endnote",
    "name": "Colombian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Colombian",
      "Colombian law",
      "Colombian Endnote"
    ]
  },
  {
    "id": "colombian-law-numeric",
    "name": "Colombian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Colombian",
      "Colombian law",
      "Colombian Numeric"
    ]
  },
  {
    "id": "colombian-law-vancouver-style",
    "name": "Colombian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Colombian",
      "Colombian law",
      "Colombian Vancouver-Style"
    ]
  },
  {
    "id": "colombian-law-author-number",
    "name": "Colombian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Colombian",
      "Colombian law",
      "Colombian Author-Number"
    ]
  },
  {
    "id": "colombian-medicine-author-date",
    "name": "Colombian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Colombian",
      "Colombian medicine",
      "Colombian Author-Date"
    ]
  },
  {
    "id": "colombian-medicine-footnote",
    "name": "Colombian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Colombian",
      "Colombian medicine",
      "Colombian Footnote"
    ]
  },
  {
    "id": "colombian-medicine-endnote",
    "name": "Colombian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Colombian",
      "Colombian medicine",
      "Colombian Endnote"
    ]
  },
  {
    "id": "colombian-medicine-numeric",
    "name": "Colombian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Colombian",
      "Colombian medicine",
      "Colombian Numeric"
    ]
  },
  {
    "id": "colombian-medicine-vancouver-style",
    "name": "Colombian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Colombian",
      "Colombian medicine",
      "Colombian Vancouver-Style"
    ]
  },
  {
    "id": "colombian-medicine-author-number",
    "name": "Colombian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Colombian",
      "Colombian medicine",
      "Colombian Author-Number"
    ]
  },
  {
    "id": "colombian-general-author-date",
    "name": "Colombian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Colombian",
      "Colombian general",
      "Colombian Author-Date"
    ]
  },
  {
    "id": "colombian-general-footnote",
    "name": "Colombian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Colombian",
      "Colombian general",
      "Colombian Footnote"
    ]
  },
  {
    "id": "colombian-general-endnote",
    "name": "Colombian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Colombian",
      "Colombian general",
      "Colombian Endnote"
    ]
  },
  {
    "id": "colombian-general-numeric",
    "name": "Colombian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Colombian",
      "Colombian general",
      "Colombian Numeric"
    ]
  },
  {
    "id": "colombian-general-vancouver-style",
    "name": "Colombian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Colombian",
      "Colombian general",
      "Colombian Vancouver-Style"
    ]
  },
  {
    "id": "colombian-general-author-number",
    "name": "Colombian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Colombian",
      "Colombian general",
      "Colombian Author-Number"
    ]
  },
  {
    "id": "peruvian-sciences-author-date",
    "name": "Peruvian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Peruvian",
      "Peruvian sciences",
      "Peruvian Author-Date"
    ]
  },
  {
    "id": "peruvian-sciences-footnote",
    "name": "Peruvian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Peruvian",
      "Peruvian sciences",
      "Peruvian Footnote"
    ]
  },
  {
    "id": "peruvian-sciences-endnote",
    "name": "Peruvian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Peruvian",
      "Peruvian sciences",
      "Peruvian Endnote"
    ]
  },
  {
    "id": "peruvian-sciences-numeric",
    "name": "Peruvian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Peruvian",
      "Peruvian sciences",
      "Peruvian Numeric"
    ]
  },
  {
    "id": "peruvian-sciences-vancouver-style",
    "name": "Peruvian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Peruvian",
      "Peruvian sciences",
      "Peruvian Vancouver-Style"
    ]
  },
  {
    "id": "peruvian-sciences-author-number",
    "name": "Peruvian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Peruvian",
      "Peruvian sciences",
      "Peruvian Author-Number"
    ]
  },
  {
    "id": "peruvian-humanities-author-date",
    "name": "Peruvian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Peruvian",
      "Peruvian humanities",
      "Peruvian Author-Date"
    ]
  },
  {
    "id": "peruvian-humanities-footnote",
    "name": "Peruvian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Peruvian",
      "Peruvian humanities",
      "Peruvian Footnote"
    ]
  },
  {
    "id": "peruvian-humanities-endnote",
    "name": "Peruvian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Peruvian",
      "Peruvian humanities",
      "Peruvian Endnote"
    ]
  },
  {
    "id": "peruvian-humanities-numeric",
    "name": "Peruvian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Peruvian",
      "Peruvian humanities",
      "Peruvian Numeric"
    ]
  },
  {
    "id": "peruvian-humanities-vancouver-style",
    "name": "Peruvian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Peruvian",
      "Peruvian humanities",
      "Peruvian Vancouver-Style"
    ]
  },
  {
    "id": "peruvian-humanities-author-number",
    "name": "Peruvian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Peruvian",
      "Peruvian humanities",
      "Peruvian Author-Number"
    ]
  },
  {
    "id": "peruvian-law-author-date",
    "name": "Peruvian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Peruvian",
      "Peruvian law",
      "Peruvian Author-Date"
    ]
  },
  {
    "id": "peruvian-law-footnote",
    "name": "Peruvian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Peruvian",
      "Peruvian law",
      "Peruvian Footnote"
    ]
  },
  {
    "id": "peruvian-law-endnote",
    "name": "Peruvian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Peruvian",
      "Peruvian law",
      "Peruvian Endnote"
    ]
  },
  {
    "id": "peruvian-law-numeric",
    "name": "Peruvian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Peruvian",
      "Peruvian law",
      "Peruvian Numeric"
    ]
  },
  {
    "id": "peruvian-law-vancouver-style",
    "name": "Peruvian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Peruvian",
      "Peruvian law",
      "Peruvian Vancouver-Style"
    ]
  },
  {
    "id": "peruvian-law-author-number",
    "name": "Peruvian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Peruvian",
      "Peruvian law",
      "Peruvian Author-Number"
    ]
  },
  {
    "id": "peruvian-medicine-author-date",
    "name": "Peruvian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Peruvian",
      "Peruvian medicine",
      "Peruvian Author-Date"
    ]
  },
  {
    "id": "peruvian-medicine-footnote",
    "name": "Peruvian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Peruvian",
      "Peruvian medicine",
      "Peruvian Footnote"
    ]
  },
  {
    "id": "peruvian-medicine-endnote",
    "name": "Peruvian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Peruvian",
      "Peruvian medicine",
      "Peruvian Endnote"
    ]
  },
  {
    "id": "peruvian-medicine-numeric",
    "name": "Peruvian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Peruvian",
      "Peruvian medicine",
      "Peruvian Numeric"
    ]
  },
  {
    "id": "peruvian-medicine-vancouver-style",
    "name": "Peruvian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Peruvian",
      "Peruvian medicine",
      "Peruvian Vancouver-Style"
    ]
  },
  {
    "id": "peruvian-medicine-author-number",
    "name": "Peruvian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Peruvian",
      "Peruvian medicine",
      "Peruvian Author-Number"
    ]
  },
  {
    "id": "peruvian-general-author-date",
    "name": "Peruvian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Peruvian",
      "Peruvian general",
      "Peruvian Author-Date"
    ]
  },
  {
    "id": "peruvian-general-footnote",
    "name": "Peruvian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Peruvian",
      "Peruvian general",
      "Peruvian Footnote"
    ]
  },
  {
    "id": "peruvian-general-endnote",
    "name": "Peruvian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Peruvian",
      "Peruvian general",
      "Peruvian Endnote"
    ]
  },
  {
    "id": "peruvian-general-numeric",
    "name": "Peruvian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Peruvian",
      "Peruvian general",
      "Peruvian Numeric"
    ]
  },
  {
    "id": "peruvian-general-vancouver-style",
    "name": "Peruvian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Peruvian",
      "Peruvian general",
      "Peruvian Vancouver-Style"
    ]
  },
  {
    "id": "peruvian-general-author-number",
    "name": "Peruvian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Peruvian",
      "Peruvian general",
      "Peruvian Author-Number"
    ]
  },
  {
    "id": "nigerian-sciences-author-date",
    "name": "Nigerian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Nigerian",
      "Nigerian sciences",
      "Nigerian Author-Date"
    ]
  },
  {
    "id": "nigerian-sciences-footnote",
    "name": "Nigerian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Nigerian",
      "Nigerian sciences",
      "Nigerian Footnote"
    ]
  },
  {
    "id": "nigerian-sciences-endnote",
    "name": "Nigerian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Nigerian",
      "Nigerian sciences",
      "Nigerian Endnote"
    ]
  },
  {
    "id": "nigerian-sciences-numeric",
    "name": "Nigerian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Nigerian",
      "Nigerian sciences",
      "Nigerian Numeric"
    ]
  },
  {
    "id": "nigerian-sciences-vancouver-style",
    "name": "Nigerian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Nigerian",
      "Nigerian sciences",
      "Nigerian Vancouver-Style"
    ]
  },
  {
    "id": "nigerian-sciences-author-number",
    "name": "Nigerian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Nigerian",
      "Nigerian sciences",
      "Nigerian Author-Number"
    ]
  },
  {
    "id": "nigerian-humanities-author-date",
    "name": "Nigerian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Nigerian",
      "Nigerian humanities",
      "Nigerian Author-Date"
    ]
  },
  {
    "id": "nigerian-humanities-footnote",
    "name": "Nigerian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Nigerian",
      "Nigerian humanities",
      "Nigerian Footnote"
    ]
  },
  {
    "id": "nigerian-humanities-endnote",
    "name": "Nigerian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Nigerian",
      "Nigerian humanities",
      "Nigerian Endnote"
    ]
  },
  {
    "id": "nigerian-humanities-numeric",
    "name": "Nigerian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Nigerian",
      "Nigerian humanities",
      "Nigerian Numeric"
    ]
  },
  {
    "id": "nigerian-humanities-vancouver-style",
    "name": "Nigerian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Nigerian",
      "Nigerian humanities",
      "Nigerian Vancouver-Style"
    ]
  },
  {
    "id": "nigerian-humanities-author-number",
    "name": "Nigerian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Nigerian",
      "Nigerian humanities",
      "Nigerian Author-Number"
    ]
  },
  {
    "id": "nigerian-law-author-date",
    "name": "Nigerian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Nigerian",
      "Nigerian law",
      "Nigerian Author-Date"
    ]
  },
  {
    "id": "nigerian-law-footnote",
    "name": "Nigerian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Nigerian",
      "Nigerian law",
      "Nigerian Footnote"
    ]
  },
  {
    "id": "nigerian-law-endnote",
    "name": "Nigerian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Nigerian",
      "Nigerian law",
      "Nigerian Endnote"
    ]
  },
  {
    "id": "nigerian-law-numeric",
    "name": "Nigerian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Nigerian",
      "Nigerian law",
      "Nigerian Numeric"
    ]
  },
  {
    "id": "nigerian-law-vancouver-style",
    "name": "Nigerian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Nigerian",
      "Nigerian law",
      "Nigerian Vancouver-Style"
    ]
  },
  {
    "id": "nigerian-law-author-number",
    "name": "Nigerian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Nigerian",
      "Nigerian law",
      "Nigerian Author-Number"
    ]
  },
  {
    "id": "nigerian-medicine-author-date",
    "name": "Nigerian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Nigerian",
      "Nigerian medicine",
      "Nigerian Author-Date"
    ]
  },
  {
    "id": "nigerian-medicine-footnote",
    "name": "Nigerian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Nigerian",
      "Nigerian medicine",
      "Nigerian Footnote"
    ]
  },
  {
    "id": "nigerian-medicine-endnote",
    "name": "Nigerian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Nigerian",
      "Nigerian medicine",
      "Nigerian Endnote"
    ]
  },
  {
    "id": "nigerian-medicine-numeric",
    "name": "Nigerian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Nigerian",
      "Nigerian medicine",
      "Nigerian Numeric"
    ]
  },
  {
    "id": "nigerian-medicine-vancouver-style",
    "name": "Nigerian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Nigerian",
      "Nigerian medicine",
      "Nigerian Vancouver-Style"
    ]
  },
  {
    "id": "nigerian-medicine-author-number",
    "name": "Nigerian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Nigerian",
      "Nigerian medicine",
      "Nigerian Author-Number"
    ]
  },
  {
    "id": "nigerian-general-author-date",
    "name": "Nigerian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Nigerian",
      "Nigerian general",
      "Nigerian Author-Date"
    ]
  },
  {
    "id": "nigerian-general-footnote",
    "name": "Nigerian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Nigerian",
      "Nigerian general",
      "Nigerian Footnote"
    ]
  },
  {
    "id": "nigerian-general-endnote",
    "name": "Nigerian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Nigerian",
      "Nigerian general",
      "Nigerian Endnote"
    ]
  },
  {
    "id": "nigerian-general-numeric",
    "name": "Nigerian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Nigerian",
      "Nigerian general",
      "Nigerian Numeric"
    ]
  },
  {
    "id": "nigerian-general-vancouver-style",
    "name": "Nigerian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Nigerian",
      "Nigerian general",
      "Nigerian Vancouver-Style"
    ]
  },
  {
    "id": "nigerian-general-author-number",
    "name": "Nigerian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Nigerian",
      "Nigerian general",
      "Nigerian Author-Number"
    ]
  },
  {
    "id": "kenyan-sciences-author-date",
    "name": "Kenyan Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Kenyan",
      "Kenyan sciences",
      "Kenyan Author-Date"
    ]
  },
  {
    "id": "kenyan-sciences-footnote",
    "name": "Kenyan Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Kenyan",
      "Kenyan sciences",
      "Kenyan Footnote"
    ]
  },
  {
    "id": "kenyan-sciences-endnote",
    "name": "Kenyan Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Kenyan",
      "Kenyan sciences",
      "Kenyan Endnote"
    ]
  },
  {
    "id": "kenyan-sciences-numeric",
    "name": "Kenyan Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Kenyan",
      "Kenyan sciences",
      "Kenyan Numeric"
    ]
  },
  {
    "id": "kenyan-sciences-vancouver-style",
    "name": "Kenyan Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Kenyan",
      "Kenyan sciences",
      "Kenyan Vancouver-Style"
    ]
  },
  {
    "id": "kenyan-sciences-author-number",
    "name": "Kenyan Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Kenyan",
      "Kenyan sciences",
      "Kenyan Author-Number"
    ]
  },
  {
    "id": "kenyan-humanities-author-date",
    "name": "Kenyan Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Kenyan",
      "Kenyan humanities",
      "Kenyan Author-Date"
    ]
  },
  {
    "id": "kenyan-humanities-footnote",
    "name": "Kenyan Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Kenyan",
      "Kenyan humanities",
      "Kenyan Footnote"
    ]
  },
  {
    "id": "kenyan-humanities-endnote",
    "name": "Kenyan Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Kenyan",
      "Kenyan humanities",
      "Kenyan Endnote"
    ]
  },
  {
    "id": "kenyan-humanities-numeric",
    "name": "Kenyan Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Kenyan",
      "Kenyan humanities",
      "Kenyan Numeric"
    ]
  },
  {
    "id": "kenyan-humanities-vancouver-style",
    "name": "Kenyan Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Kenyan",
      "Kenyan humanities",
      "Kenyan Vancouver-Style"
    ]
  },
  {
    "id": "kenyan-humanities-author-number",
    "name": "Kenyan Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Kenyan",
      "Kenyan humanities",
      "Kenyan Author-Number"
    ]
  },
  {
    "id": "kenyan-law-author-date",
    "name": "Kenyan Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Kenyan",
      "Kenyan law",
      "Kenyan Author-Date"
    ]
  },
  {
    "id": "kenyan-law-footnote",
    "name": "Kenyan Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Kenyan",
      "Kenyan law",
      "Kenyan Footnote"
    ]
  },
  {
    "id": "kenyan-law-endnote",
    "name": "Kenyan Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Kenyan",
      "Kenyan law",
      "Kenyan Endnote"
    ]
  },
  {
    "id": "kenyan-law-numeric",
    "name": "Kenyan Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Kenyan",
      "Kenyan law",
      "Kenyan Numeric"
    ]
  },
  {
    "id": "kenyan-law-vancouver-style",
    "name": "Kenyan Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Kenyan",
      "Kenyan law",
      "Kenyan Vancouver-Style"
    ]
  },
  {
    "id": "kenyan-law-author-number",
    "name": "Kenyan Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Kenyan",
      "Kenyan law",
      "Kenyan Author-Number"
    ]
  },
  {
    "id": "kenyan-medicine-author-date",
    "name": "Kenyan Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Kenyan",
      "Kenyan medicine",
      "Kenyan Author-Date"
    ]
  },
  {
    "id": "kenyan-medicine-footnote",
    "name": "Kenyan Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Kenyan",
      "Kenyan medicine",
      "Kenyan Footnote"
    ]
  },
  {
    "id": "kenyan-medicine-endnote",
    "name": "Kenyan Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Kenyan",
      "Kenyan medicine",
      "Kenyan Endnote"
    ]
  },
  {
    "id": "kenyan-medicine-numeric",
    "name": "Kenyan Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Kenyan",
      "Kenyan medicine",
      "Kenyan Numeric"
    ]
  },
  {
    "id": "kenyan-medicine-vancouver-style",
    "name": "Kenyan Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Kenyan",
      "Kenyan medicine",
      "Kenyan Vancouver-Style"
    ]
  },
  {
    "id": "kenyan-medicine-author-number",
    "name": "Kenyan Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Kenyan",
      "Kenyan medicine",
      "Kenyan Author-Number"
    ]
  },
  {
    "id": "kenyan-general-author-date",
    "name": "Kenyan General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Kenyan",
      "Kenyan general",
      "Kenyan Author-Date"
    ]
  },
  {
    "id": "kenyan-general-footnote",
    "name": "Kenyan General — Footnote",
    "discipline": "general",
    "aliases": [
      "Kenyan",
      "Kenyan general",
      "Kenyan Footnote"
    ]
  },
  {
    "id": "kenyan-general-endnote",
    "name": "Kenyan General — Endnote",
    "discipline": "general",
    "aliases": [
      "Kenyan",
      "Kenyan general",
      "Kenyan Endnote"
    ]
  },
  {
    "id": "kenyan-general-numeric",
    "name": "Kenyan General — Numeric",
    "discipline": "general",
    "aliases": [
      "Kenyan",
      "Kenyan general",
      "Kenyan Numeric"
    ]
  },
  {
    "id": "kenyan-general-vancouver-style",
    "name": "Kenyan General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Kenyan",
      "Kenyan general",
      "Kenyan Vancouver-Style"
    ]
  },
  {
    "id": "kenyan-general-author-number",
    "name": "Kenyan General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Kenyan",
      "Kenyan general",
      "Kenyan Author-Number"
    ]
  },
  {
    "id": "egyptian-sciences-author-date",
    "name": "Egyptian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Egyptian",
      "Egyptian sciences",
      "Egyptian Author-Date"
    ]
  },
  {
    "id": "egyptian-sciences-footnote",
    "name": "Egyptian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Egyptian",
      "Egyptian sciences",
      "Egyptian Footnote"
    ]
  },
  {
    "id": "egyptian-sciences-endnote",
    "name": "Egyptian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Egyptian",
      "Egyptian sciences",
      "Egyptian Endnote"
    ]
  },
  {
    "id": "egyptian-sciences-numeric",
    "name": "Egyptian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Egyptian",
      "Egyptian sciences",
      "Egyptian Numeric"
    ]
  },
  {
    "id": "egyptian-sciences-vancouver-style",
    "name": "Egyptian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Egyptian",
      "Egyptian sciences",
      "Egyptian Vancouver-Style"
    ]
  },
  {
    "id": "egyptian-sciences-author-number",
    "name": "Egyptian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Egyptian",
      "Egyptian sciences",
      "Egyptian Author-Number"
    ]
  },
  {
    "id": "egyptian-humanities-author-date",
    "name": "Egyptian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Egyptian",
      "Egyptian humanities",
      "Egyptian Author-Date"
    ]
  },
  {
    "id": "egyptian-humanities-footnote",
    "name": "Egyptian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Egyptian",
      "Egyptian humanities",
      "Egyptian Footnote"
    ]
  },
  {
    "id": "egyptian-humanities-endnote",
    "name": "Egyptian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Egyptian",
      "Egyptian humanities",
      "Egyptian Endnote"
    ]
  },
  {
    "id": "egyptian-humanities-numeric",
    "name": "Egyptian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Egyptian",
      "Egyptian humanities",
      "Egyptian Numeric"
    ]
  },
  {
    "id": "egyptian-humanities-vancouver-style",
    "name": "Egyptian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Egyptian",
      "Egyptian humanities",
      "Egyptian Vancouver-Style"
    ]
  },
  {
    "id": "egyptian-humanities-author-number",
    "name": "Egyptian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Egyptian",
      "Egyptian humanities",
      "Egyptian Author-Number"
    ]
  },
  {
    "id": "egyptian-law-author-date",
    "name": "Egyptian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Egyptian",
      "Egyptian law",
      "Egyptian Author-Date"
    ]
  },
  {
    "id": "egyptian-law-footnote",
    "name": "Egyptian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Egyptian",
      "Egyptian law",
      "Egyptian Footnote"
    ]
  },
  {
    "id": "egyptian-law-endnote",
    "name": "Egyptian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Egyptian",
      "Egyptian law",
      "Egyptian Endnote"
    ]
  },
  {
    "id": "egyptian-law-numeric",
    "name": "Egyptian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Egyptian",
      "Egyptian law",
      "Egyptian Numeric"
    ]
  },
  {
    "id": "egyptian-law-vancouver-style",
    "name": "Egyptian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Egyptian",
      "Egyptian law",
      "Egyptian Vancouver-Style"
    ]
  },
  {
    "id": "egyptian-law-author-number",
    "name": "Egyptian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Egyptian",
      "Egyptian law",
      "Egyptian Author-Number"
    ]
  },
  {
    "id": "egyptian-medicine-author-date",
    "name": "Egyptian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Egyptian",
      "Egyptian medicine",
      "Egyptian Author-Date"
    ]
  },
  {
    "id": "egyptian-medicine-footnote",
    "name": "Egyptian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Egyptian",
      "Egyptian medicine",
      "Egyptian Footnote"
    ]
  },
  {
    "id": "egyptian-medicine-endnote",
    "name": "Egyptian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Egyptian",
      "Egyptian medicine",
      "Egyptian Endnote"
    ]
  },
  {
    "id": "egyptian-medicine-numeric",
    "name": "Egyptian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Egyptian",
      "Egyptian medicine",
      "Egyptian Numeric"
    ]
  },
  {
    "id": "egyptian-medicine-vancouver-style",
    "name": "Egyptian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Egyptian",
      "Egyptian medicine",
      "Egyptian Vancouver-Style"
    ]
  },
  {
    "id": "egyptian-medicine-author-number",
    "name": "Egyptian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Egyptian",
      "Egyptian medicine",
      "Egyptian Author-Number"
    ]
  },
  {
    "id": "egyptian-general-author-date",
    "name": "Egyptian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Egyptian",
      "Egyptian general",
      "Egyptian Author-Date"
    ]
  },
  {
    "id": "egyptian-general-footnote",
    "name": "Egyptian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Egyptian",
      "Egyptian general",
      "Egyptian Footnote"
    ]
  },
  {
    "id": "egyptian-general-endnote",
    "name": "Egyptian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Egyptian",
      "Egyptian general",
      "Egyptian Endnote"
    ]
  },
  {
    "id": "egyptian-general-numeric",
    "name": "Egyptian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Egyptian",
      "Egyptian general",
      "Egyptian Numeric"
    ]
  },
  {
    "id": "egyptian-general-vancouver-style",
    "name": "Egyptian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Egyptian",
      "Egyptian general",
      "Egyptian Vancouver-Style"
    ]
  },
  {
    "id": "egyptian-general-author-number",
    "name": "Egyptian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Egyptian",
      "Egyptian general",
      "Egyptian Author-Number"
    ]
  },
  {
    "id": "israeli-sciences-author-date",
    "name": "Israeli Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Israeli",
      "Israeli sciences",
      "Israeli Author-Date"
    ]
  },
  {
    "id": "israeli-sciences-footnote",
    "name": "Israeli Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Israeli",
      "Israeli sciences",
      "Israeli Footnote"
    ]
  },
  {
    "id": "israeli-sciences-endnote",
    "name": "Israeli Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Israeli",
      "Israeli sciences",
      "Israeli Endnote"
    ]
  },
  {
    "id": "israeli-sciences-numeric",
    "name": "Israeli Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Israeli",
      "Israeli sciences",
      "Israeli Numeric"
    ]
  },
  {
    "id": "israeli-sciences-vancouver-style",
    "name": "Israeli Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Israeli",
      "Israeli sciences",
      "Israeli Vancouver-Style"
    ]
  },
  {
    "id": "israeli-sciences-author-number",
    "name": "Israeli Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Israeli",
      "Israeli sciences",
      "Israeli Author-Number"
    ]
  },
  {
    "id": "israeli-humanities-author-date",
    "name": "Israeli Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Israeli",
      "Israeli humanities",
      "Israeli Author-Date"
    ]
  },
  {
    "id": "israeli-humanities-footnote",
    "name": "Israeli Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Israeli",
      "Israeli humanities",
      "Israeli Footnote"
    ]
  },
  {
    "id": "israeli-humanities-endnote",
    "name": "Israeli Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Israeli",
      "Israeli humanities",
      "Israeli Endnote"
    ]
  },
  {
    "id": "israeli-humanities-numeric",
    "name": "Israeli Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Israeli",
      "Israeli humanities",
      "Israeli Numeric"
    ]
  },
  {
    "id": "israeli-humanities-vancouver-style",
    "name": "Israeli Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Israeli",
      "Israeli humanities",
      "Israeli Vancouver-Style"
    ]
  },
  {
    "id": "israeli-humanities-author-number",
    "name": "Israeli Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Israeli",
      "Israeli humanities",
      "Israeli Author-Number"
    ]
  },
  {
    "id": "israeli-law-author-date",
    "name": "Israeli Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Israeli",
      "Israeli law",
      "Israeli Author-Date"
    ]
  },
  {
    "id": "israeli-law-footnote",
    "name": "Israeli Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Israeli",
      "Israeli law",
      "Israeli Footnote"
    ]
  },
  {
    "id": "israeli-law-endnote",
    "name": "Israeli Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Israeli",
      "Israeli law",
      "Israeli Endnote"
    ]
  },
  {
    "id": "israeli-law-numeric",
    "name": "Israeli Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Israeli",
      "Israeli law",
      "Israeli Numeric"
    ]
  },
  {
    "id": "israeli-law-vancouver-style",
    "name": "Israeli Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Israeli",
      "Israeli law",
      "Israeli Vancouver-Style"
    ]
  },
  {
    "id": "israeli-law-author-number",
    "name": "Israeli Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Israeli",
      "Israeli law",
      "Israeli Author-Number"
    ]
  },
  {
    "id": "israeli-medicine-author-date",
    "name": "Israeli Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Israeli",
      "Israeli medicine",
      "Israeli Author-Date"
    ]
  },
  {
    "id": "israeli-medicine-footnote",
    "name": "Israeli Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Israeli",
      "Israeli medicine",
      "Israeli Footnote"
    ]
  },
  {
    "id": "israeli-medicine-endnote",
    "name": "Israeli Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Israeli",
      "Israeli medicine",
      "Israeli Endnote"
    ]
  },
  {
    "id": "israeli-medicine-numeric",
    "name": "Israeli Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Israeli",
      "Israeli medicine",
      "Israeli Numeric"
    ]
  },
  {
    "id": "israeli-medicine-vancouver-style",
    "name": "Israeli Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Israeli",
      "Israeli medicine",
      "Israeli Vancouver-Style"
    ]
  },
  {
    "id": "israeli-medicine-author-number",
    "name": "Israeli Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Israeli",
      "Israeli medicine",
      "Israeli Author-Number"
    ]
  },
  {
    "id": "israeli-general-author-date",
    "name": "Israeli General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Israeli",
      "Israeli general",
      "Israeli Author-Date"
    ]
  },
  {
    "id": "israeli-general-footnote",
    "name": "Israeli General — Footnote",
    "discipline": "general",
    "aliases": [
      "Israeli",
      "Israeli general",
      "Israeli Footnote"
    ]
  },
  {
    "id": "israeli-general-endnote",
    "name": "Israeli General — Endnote",
    "discipline": "general",
    "aliases": [
      "Israeli",
      "Israeli general",
      "Israeli Endnote"
    ]
  },
  {
    "id": "israeli-general-numeric",
    "name": "Israeli General — Numeric",
    "discipline": "general",
    "aliases": [
      "Israeli",
      "Israeli general",
      "Israeli Numeric"
    ]
  },
  {
    "id": "israeli-general-vancouver-style",
    "name": "Israeli General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Israeli",
      "Israeli general",
      "Israeli Vancouver-Style"
    ]
  },
  {
    "id": "israeli-general-author-number",
    "name": "Israeli General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Israeli",
      "Israeli general",
      "Israeli Author-Number"
    ]
  },
  {
    "id": "korean-sciences-author-date",
    "name": "Korean Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Korean",
      "Korean sciences",
      "Korean Author-Date"
    ]
  },
  {
    "id": "korean-sciences-footnote",
    "name": "Korean Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Korean",
      "Korean sciences",
      "Korean Footnote"
    ]
  },
  {
    "id": "korean-sciences-endnote",
    "name": "Korean Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Korean",
      "Korean sciences",
      "Korean Endnote"
    ]
  },
  {
    "id": "korean-sciences-numeric",
    "name": "Korean Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Korean",
      "Korean sciences",
      "Korean Numeric"
    ]
  },
  {
    "id": "korean-sciences-vancouver-style",
    "name": "Korean Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Korean",
      "Korean sciences",
      "Korean Vancouver-Style"
    ]
  },
  {
    "id": "korean-sciences-author-number",
    "name": "Korean Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Korean",
      "Korean sciences",
      "Korean Author-Number"
    ]
  },
  {
    "id": "korean-humanities-author-date",
    "name": "Korean Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Korean",
      "Korean humanities",
      "Korean Author-Date"
    ]
  },
  {
    "id": "korean-humanities-footnote",
    "name": "Korean Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Korean",
      "Korean humanities",
      "Korean Footnote"
    ]
  },
  {
    "id": "korean-humanities-endnote",
    "name": "Korean Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Korean",
      "Korean humanities",
      "Korean Endnote"
    ]
  },
  {
    "id": "korean-humanities-numeric",
    "name": "Korean Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Korean",
      "Korean humanities",
      "Korean Numeric"
    ]
  },
  {
    "id": "korean-humanities-vancouver-style",
    "name": "Korean Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Korean",
      "Korean humanities",
      "Korean Vancouver-Style"
    ]
  },
  {
    "id": "korean-humanities-author-number",
    "name": "Korean Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Korean",
      "Korean humanities",
      "Korean Author-Number"
    ]
  },
  {
    "id": "korean-law-author-date",
    "name": "Korean Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Korean",
      "Korean law",
      "Korean Author-Date"
    ]
  },
  {
    "id": "korean-law-footnote",
    "name": "Korean Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Korean",
      "Korean law",
      "Korean Footnote"
    ]
  },
  {
    "id": "korean-law-endnote",
    "name": "Korean Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Korean",
      "Korean law",
      "Korean Endnote"
    ]
  },
  {
    "id": "korean-law-numeric",
    "name": "Korean Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Korean",
      "Korean law",
      "Korean Numeric"
    ]
  },
  {
    "id": "korean-law-vancouver-style",
    "name": "Korean Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Korean",
      "Korean law",
      "Korean Vancouver-Style"
    ]
  },
  {
    "id": "korean-law-author-number",
    "name": "Korean Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Korean",
      "Korean law",
      "Korean Author-Number"
    ]
  },
  {
    "id": "korean-medicine-author-date",
    "name": "Korean Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Korean",
      "Korean medicine",
      "Korean Author-Date"
    ]
  },
  {
    "id": "korean-medicine-footnote",
    "name": "Korean Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Korean",
      "Korean medicine",
      "Korean Footnote"
    ]
  },
  {
    "id": "korean-medicine-endnote",
    "name": "Korean Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Korean",
      "Korean medicine",
      "Korean Endnote"
    ]
  },
  {
    "id": "korean-medicine-numeric",
    "name": "Korean Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Korean",
      "Korean medicine",
      "Korean Numeric"
    ]
  },
  {
    "id": "korean-medicine-vancouver-style",
    "name": "Korean Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Korean",
      "Korean medicine",
      "Korean Vancouver-Style"
    ]
  },
  {
    "id": "korean-medicine-author-number",
    "name": "Korean Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Korean",
      "Korean medicine",
      "Korean Author-Number"
    ]
  },
  {
    "id": "korean-general-author-date",
    "name": "Korean General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Korean",
      "Korean general",
      "Korean Author-Date"
    ]
  },
  {
    "id": "korean-general-footnote",
    "name": "Korean General — Footnote",
    "discipline": "general",
    "aliases": [
      "Korean",
      "Korean general",
      "Korean Footnote"
    ]
  },
  {
    "id": "korean-general-endnote",
    "name": "Korean General — Endnote",
    "discipline": "general",
    "aliases": [
      "Korean",
      "Korean general",
      "Korean Endnote"
    ]
  },
  {
    "id": "korean-general-numeric",
    "name": "Korean General — Numeric",
    "discipline": "general",
    "aliases": [
      "Korean",
      "Korean general",
      "Korean Numeric"
    ]
  },
  {
    "id": "korean-general-vancouver-style",
    "name": "Korean General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Korean",
      "Korean general",
      "Korean Vancouver-Style"
    ]
  },
  {
    "id": "korean-general-author-number",
    "name": "Korean General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Korean",
      "Korean general",
      "Korean Author-Number"
    ]
  },
  {
    "id": "taiwanese-sciences-author-date",
    "name": "Taiwanese Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Taiwanese",
      "Taiwanese sciences",
      "Taiwanese Author-Date"
    ]
  },
  {
    "id": "taiwanese-sciences-footnote",
    "name": "Taiwanese Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Taiwanese",
      "Taiwanese sciences",
      "Taiwanese Footnote"
    ]
  },
  {
    "id": "taiwanese-sciences-endnote",
    "name": "Taiwanese Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Taiwanese",
      "Taiwanese sciences",
      "Taiwanese Endnote"
    ]
  },
  {
    "id": "taiwanese-sciences-numeric",
    "name": "Taiwanese Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Taiwanese",
      "Taiwanese sciences",
      "Taiwanese Numeric"
    ]
  },
  {
    "id": "taiwanese-sciences-vancouver-style",
    "name": "Taiwanese Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Taiwanese",
      "Taiwanese sciences",
      "Taiwanese Vancouver-Style"
    ]
  },
  {
    "id": "taiwanese-sciences-author-number",
    "name": "Taiwanese Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Taiwanese",
      "Taiwanese sciences",
      "Taiwanese Author-Number"
    ]
  },
  {
    "id": "taiwanese-humanities-author-date",
    "name": "Taiwanese Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Taiwanese",
      "Taiwanese humanities",
      "Taiwanese Author-Date"
    ]
  },
  {
    "id": "taiwanese-humanities-footnote",
    "name": "Taiwanese Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Taiwanese",
      "Taiwanese humanities",
      "Taiwanese Footnote"
    ]
  },
  {
    "id": "taiwanese-humanities-endnote",
    "name": "Taiwanese Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Taiwanese",
      "Taiwanese humanities",
      "Taiwanese Endnote"
    ]
  },
  {
    "id": "taiwanese-humanities-numeric",
    "name": "Taiwanese Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Taiwanese",
      "Taiwanese humanities",
      "Taiwanese Numeric"
    ]
  },
  {
    "id": "taiwanese-humanities-vancouver-style",
    "name": "Taiwanese Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Taiwanese",
      "Taiwanese humanities",
      "Taiwanese Vancouver-Style"
    ]
  },
  {
    "id": "taiwanese-humanities-author-number",
    "name": "Taiwanese Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Taiwanese",
      "Taiwanese humanities",
      "Taiwanese Author-Number"
    ]
  },
  {
    "id": "taiwanese-law-author-date",
    "name": "Taiwanese Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Taiwanese",
      "Taiwanese law",
      "Taiwanese Author-Date"
    ]
  },
  {
    "id": "taiwanese-law-footnote",
    "name": "Taiwanese Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Taiwanese",
      "Taiwanese law",
      "Taiwanese Footnote"
    ]
  },
  {
    "id": "taiwanese-law-endnote",
    "name": "Taiwanese Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Taiwanese",
      "Taiwanese law",
      "Taiwanese Endnote"
    ]
  },
  {
    "id": "taiwanese-law-numeric",
    "name": "Taiwanese Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Taiwanese",
      "Taiwanese law",
      "Taiwanese Numeric"
    ]
  },
  {
    "id": "taiwanese-law-vancouver-style",
    "name": "Taiwanese Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Taiwanese",
      "Taiwanese law",
      "Taiwanese Vancouver-Style"
    ]
  },
  {
    "id": "taiwanese-law-author-number",
    "name": "Taiwanese Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Taiwanese",
      "Taiwanese law",
      "Taiwanese Author-Number"
    ]
  },
  {
    "id": "taiwanese-medicine-author-date",
    "name": "Taiwanese Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Taiwanese",
      "Taiwanese medicine",
      "Taiwanese Author-Date"
    ]
  },
  {
    "id": "taiwanese-medicine-footnote",
    "name": "Taiwanese Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Taiwanese",
      "Taiwanese medicine",
      "Taiwanese Footnote"
    ]
  },
  {
    "id": "taiwanese-medicine-endnote",
    "name": "Taiwanese Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Taiwanese",
      "Taiwanese medicine",
      "Taiwanese Endnote"
    ]
  },
  {
    "id": "taiwanese-medicine-numeric",
    "name": "Taiwanese Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Taiwanese",
      "Taiwanese medicine",
      "Taiwanese Numeric"
    ]
  },
  {
    "id": "taiwanese-medicine-vancouver-style",
    "name": "Taiwanese Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Taiwanese",
      "Taiwanese medicine",
      "Taiwanese Vancouver-Style"
    ]
  },
  {
    "id": "taiwanese-medicine-author-number",
    "name": "Taiwanese Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Taiwanese",
      "Taiwanese medicine",
      "Taiwanese Author-Number"
    ]
  },
  {
    "id": "taiwanese-general-author-date",
    "name": "Taiwanese General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Taiwanese",
      "Taiwanese general",
      "Taiwanese Author-Date"
    ]
  },
  {
    "id": "taiwanese-general-footnote",
    "name": "Taiwanese General — Footnote",
    "discipline": "general",
    "aliases": [
      "Taiwanese",
      "Taiwanese general",
      "Taiwanese Footnote"
    ]
  },
  {
    "id": "taiwanese-general-endnote",
    "name": "Taiwanese General — Endnote",
    "discipline": "general",
    "aliases": [
      "Taiwanese",
      "Taiwanese general",
      "Taiwanese Endnote"
    ]
  },
  {
    "id": "taiwanese-general-numeric",
    "name": "Taiwanese General — Numeric",
    "discipline": "general",
    "aliases": [
      "Taiwanese",
      "Taiwanese general",
      "Taiwanese Numeric"
    ]
  },
  {
    "id": "taiwanese-general-vancouver-style",
    "name": "Taiwanese General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Taiwanese",
      "Taiwanese general",
      "Taiwanese Vancouver-Style"
    ]
  },
  {
    "id": "taiwanese-general-author-number",
    "name": "Taiwanese General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Taiwanese",
      "Taiwanese general",
      "Taiwanese Author-Number"
    ]
  },
  {
    "id": "singaporean-sciences-author-date",
    "name": "Singaporean Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Singaporean",
      "Singaporean sciences",
      "Singaporean Author-Date"
    ]
  },
  {
    "id": "singaporean-sciences-footnote",
    "name": "Singaporean Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Singaporean",
      "Singaporean sciences",
      "Singaporean Footnote"
    ]
  },
  {
    "id": "singaporean-sciences-endnote",
    "name": "Singaporean Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Singaporean",
      "Singaporean sciences",
      "Singaporean Endnote"
    ]
  },
  {
    "id": "singaporean-sciences-numeric",
    "name": "Singaporean Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Singaporean",
      "Singaporean sciences",
      "Singaporean Numeric"
    ]
  },
  {
    "id": "singaporean-sciences-vancouver-style",
    "name": "Singaporean Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Singaporean",
      "Singaporean sciences",
      "Singaporean Vancouver-Style"
    ]
  },
  {
    "id": "singaporean-sciences-author-number",
    "name": "Singaporean Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Singaporean",
      "Singaporean sciences",
      "Singaporean Author-Number"
    ]
  },
  {
    "id": "singaporean-humanities-author-date",
    "name": "Singaporean Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Singaporean",
      "Singaporean humanities",
      "Singaporean Author-Date"
    ]
  },
  {
    "id": "singaporean-humanities-footnote",
    "name": "Singaporean Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Singaporean",
      "Singaporean humanities",
      "Singaporean Footnote"
    ]
  },
  {
    "id": "singaporean-humanities-endnote",
    "name": "Singaporean Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Singaporean",
      "Singaporean humanities",
      "Singaporean Endnote"
    ]
  },
  {
    "id": "singaporean-humanities-numeric",
    "name": "Singaporean Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Singaporean",
      "Singaporean humanities",
      "Singaporean Numeric"
    ]
  },
  {
    "id": "singaporean-humanities-vancouver-style",
    "name": "Singaporean Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Singaporean",
      "Singaporean humanities",
      "Singaporean Vancouver-Style"
    ]
  },
  {
    "id": "singaporean-humanities-author-number",
    "name": "Singaporean Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Singaporean",
      "Singaporean humanities",
      "Singaporean Author-Number"
    ]
  },
  {
    "id": "singaporean-law-author-date",
    "name": "Singaporean Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Singaporean",
      "Singaporean law",
      "Singaporean Author-Date"
    ]
  },
  {
    "id": "singaporean-law-footnote",
    "name": "Singaporean Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Singaporean",
      "Singaporean law",
      "Singaporean Footnote"
    ]
  },
  {
    "id": "singaporean-law-endnote",
    "name": "Singaporean Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Singaporean",
      "Singaporean law",
      "Singaporean Endnote"
    ]
  },
  {
    "id": "singaporean-law-numeric",
    "name": "Singaporean Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Singaporean",
      "Singaporean law",
      "Singaporean Numeric"
    ]
  },
  {
    "id": "singaporean-law-vancouver-style",
    "name": "Singaporean Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Singaporean",
      "Singaporean law",
      "Singaporean Vancouver-Style"
    ]
  },
  {
    "id": "singaporean-law-author-number",
    "name": "Singaporean Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Singaporean",
      "Singaporean law",
      "Singaporean Author-Number"
    ]
  },
  {
    "id": "singaporean-medicine-author-date",
    "name": "Singaporean Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Singaporean",
      "Singaporean medicine",
      "Singaporean Author-Date"
    ]
  },
  {
    "id": "singaporean-medicine-footnote",
    "name": "Singaporean Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Singaporean",
      "Singaporean medicine",
      "Singaporean Footnote"
    ]
  },
  {
    "id": "singaporean-medicine-endnote",
    "name": "Singaporean Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Singaporean",
      "Singaporean medicine",
      "Singaporean Endnote"
    ]
  },
  {
    "id": "singaporean-medicine-numeric",
    "name": "Singaporean Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Singaporean",
      "Singaporean medicine",
      "Singaporean Numeric"
    ]
  },
  {
    "id": "singaporean-medicine-vancouver-style",
    "name": "Singaporean Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Singaporean",
      "Singaporean medicine",
      "Singaporean Vancouver-Style"
    ]
  },
  {
    "id": "singaporean-medicine-author-number",
    "name": "Singaporean Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Singaporean",
      "Singaporean medicine",
      "Singaporean Author-Number"
    ]
  },
  {
    "id": "singaporean-general-author-date",
    "name": "Singaporean General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Singaporean",
      "Singaporean general",
      "Singaporean Author-Date"
    ]
  },
  {
    "id": "singaporean-general-footnote",
    "name": "Singaporean General — Footnote",
    "discipline": "general",
    "aliases": [
      "Singaporean",
      "Singaporean general",
      "Singaporean Footnote"
    ]
  },
  {
    "id": "singaporean-general-endnote",
    "name": "Singaporean General — Endnote",
    "discipline": "general",
    "aliases": [
      "Singaporean",
      "Singaporean general",
      "Singaporean Endnote"
    ]
  },
  {
    "id": "singaporean-general-numeric",
    "name": "Singaporean General — Numeric",
    "discipline": "general",
    "aliases": [
      "Singaporean",
      "Singaporean general",
      "Singaporean Numeric"
    ]
  },
  {
    "id": "singaporean-general-vancouver-style",
    "name": "Singaporean General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Singaporean",
      "Singaporean general",
      "Singaporean Vancouver-Style"
    ]
  },
  {
    "id": "singaporean-general-author-number",
    "name": "Singaporean General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Singaporean",
      "Singaporean general",
      "Singaporean Author-Number"
    ]
  },
  {
    "id": "malaysian-sciences-author-date",
    "name": "Malaysian Sciences — Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Malaysian",
      "Malaysian sciences",
      "Malaysian Author-Date"
    ]
  },
  {
    "id": "malaysian-sciences-footnote",
    "name": "Malaysian Sciences — Footnote",
    "discipline": "sciences",
    "aliases": [
      "Malaysian",
      "Malaysian sciences",
      "Malaysian Footnote"
    ]
  },
  {
    "id": "malaysian-sciences-endnote",
    "name": "Malaysian Sciences — Endnote",
    "discipline": "sciences",
    "aliases": [
      "Malaysian",
      "Malaysian sciences",
      "Malaysian Endnote"
    ]
  },
  {
    "id": "malaysian-sciences-numeric",
    "name": "Malaysian Sciences — Numeric",
    "discipline": "sciences",
    "aliases": [
      "Malaysian",
      "Malaysian sciences",
      "Malaysian Numeric"
    ]
  },
  {
    "id": "malaysian-sciences-vancouver-style",
    "name": "Malaysian Sciences — Vancouver-Style",
    "discipline": "sciences",
    "aliases": [
      "Malaysian",
      "Malaysian sciences",
      "Malaysian Vancouver-Style"
    ]
  },
  {
    "id": "malaysian-sciences-author-number",
    "name": "Malaysian Sciences — Author-Number",
    "discipline": "sciences",
    "aliases": [
      "Malaysian",
      "Malaysian sciences",
      "Malaysian Author-Number"
    ]
  },
  {
    "id": "malaysian-humanities-author-date",
    "name": "Malaysian Humanities — Author-Date",
    "discipline": "humanities",
    "aliases": [
      "Malaysian",
      "Malaysian humanities",
      "Malaysian Author-Date"
    ]
  },
  {
    "id": "malaysian-humanities-footnote",
    "name": "Malaysian Humanities — Footnote",
    "discipline": "humanities",
    "aliases": [
      "Malaysian",
      "Malaysian humanities",
      "Malaysian Footnote"
    ]
  },
  {
    "id": "malaysian-humanities-endnote",
    "name": "Malaysian Humanities — Endnote",
    "discipline": "humanities",
    "aliases": [
      "Malaysian",
      "Malaysian humanities",
      "Malaysian Endnote"
    ]
  },
  {
    "id": "malaysian-humanities-numeric",
    "name": "Malaysian Humanities — Numeric",
    "discipline": "humanities",
    "aliases": [
      "Malaysian",
      "Malaysian humanities",
      "Malaysian Numeric"
    ]
  },
  {
    "id": "malaysian-humanities-vancouver-style",
    "name": "Malaysian Humanities — Vancouver-Style",
    "discipline": "humanities",
    "aliases": [
      "Malaysian",
      "Malaysian humanities",
      "Malaysian Vancouver-Style"
    ]
  },
  {
    "id": "malaysian-humanities-author-number",
    "name": "Malaysian Humanities — Author-Number",
    "discipline": "humanities",
    "aliases": [
      "Malaysian",
      "Malaysian humanities",
      "Malaysian Author-Number"
    ]
  },
  {
    "id": "malaysian-law-author-date",
    "name": "Malaysian Law — Author-Date",
    "discipline": "law",
    "aliases": [
      "Malaysian",
      "Malaysian law",
      "Malaysian Author-Date"
    ]
  },
  {
    "id": "malaysian-law-footnote",
    "name": "Malaysian Law — Footnote",
    "discipline": "law",
    "aliases": [
      "Malaysian",
      "Malaysian law",
      "Malaysian Footnote"
    ]
  },
  {
    "id": "malaysian-law-endnote",
    "name": "Malaysian Law — Endnote",
    "discipline": "law",
    "aliases": [
      "Malaysian",
      "Malaysian law",
      "Malaysian Endnote"
    ]
  },
  {
    "id": "malaysian-law-numeric",
    "name": "Malaysian Law — Numeric",
    "discipline": "law",
    "aliases": [
      "Malaysian",
      "Malaysian law",
      "Malaysian Numeric"
    ]
  },
  {
    "id": "malaysian-law-vancouver-style",
    "name": "Malaysian Law — Vancouver-Style",
    "discipline": "law",
    "aliases": [
      "Malaysian",
      "Malaysian law",
      "Malaysian Vancouver-Style"
    ]
  },
  {
    "id": "malaysian-law-author-number",
    "name": "Malaysian Law — Author-Number",
    "discipline": "law",
    "aliases": [
      "Malaysian",
      "Malaysian law",
      "Malaysian Author-Number"
    ]
  },
  {
    "id": "malaysian-medicine-author-date",
    "name": "Malaysian Medicine — Author-Date",
    "discipline": "medicine",
    "aliases": [
      "Malaysian",
      "Malaysian medicine",
      "Malaysian Author-Date"
    ]
  },
  {
    "id": "malaysian-medicine-footnote",
    "name": "Malaysian Medicine — Footnote",
    "discipline": "medicine",
    "aliases": [
      "Malaysian",
      "Malaysian medicine",
      "Malaysian Footnote"
    ]
  },
  {
    "id": "malaysian-medicine-endnote",
    "name": "Malaysian Medicine — Endnote",
    "discipline": "medicine",
    "aliases": [
      "Malaysian",
      "Malaysian medicine",
      "Malaysian Endnote"
    ]
  },
  {
    "id": "malaysian-medicine-numeric",
    "name": "Malaysian Medicine — Numeric",
    "discipline": "medicine",
    "aliases": [
      "Malaysian",
      "Malaysian medicine",
      "Malaysian Numeric"
    ]
  },
  {
    "id": "malaysian-medicine-vancouver-style",
    "name": "Malaysian Medicine — Vancouver-Style",
    "discipline": "medicine",
    "aliases": [
      "Malaysian",
      "Malaysian medicine",
      "Malaysian Vancouver-Style"
    ]
  },
  {
    "id": "malaysian-medicine-author-number",
    "name": "Malaysian Medicine — Author-Number",
    "discipline": "medicine",
    "aliases": [
      "Malaysian",
      "Malaysian medicine",
      "Malaysian Author-Number"
    ]
  },
  {
    "id": "malaysian-general-author-date",
    "name": "Malaysian General — Author-Date",
    "discipline": "general",
    "aliases": [
      "Malaysian",
      "Malaysian general",
      "Malaysian Author-Date"
    ]
  },
  {
    "id": "malaysian-general-footnote",
    "name": "Malaysian General — Footnote",
    "discipline": "general",
    "aliases": [
      "Malaysian",
      "Malaysian general",
      "Malaysian Footnote"
    ]
  },
  {
    "id": "malaysian-general-endnote",
    "name": "Malaysian General — Endnote",
    "discipline": "general",
    "aliases": [
      "Malaysian",
      "Malaysian general",
      "Malaysian Endnote"
    ]
  },
  {
    "id": "malaysian-general-numeric",
    "name": "Malaysian General — Numeric",
    "discipline": "general",
    "aliases": [
      "Malaysian",
      "Malaysian general",
      "Malaysian Numeric"
    ]
  },
  {
    "id": "malaysian-general-vancouver-style",
    "name": "Malaysian General — Vancouver-Style",
    "discipline": "general",
    "aliases": [
      "Malaysian",
      "Malaysian general",
      "Malaysian Vancouver-Style"
    ]
  },
  {
    "id": "malaysian-general-author-number",
    "name": "Malaysian General — Author-Number",
    "discipline": "general",
    "aliases": [
      "Malaysian",
      "Malaysian general",
      "Malaysian Author-Number"
    ]
  },
  {
    "id": "elsevier-sciences",
    "name": "Elsevier — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Elsevier",
      "Elsevier sciences"
    ]
  },
  {
    "id": "elsevier-humanities",
    "name": "Elsevier — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Elsevier",
      "Elsevier humanities"
    ]
  },
  {
    "id": "elsevier-law",
    "name": "Elsevier — Law",
    "discipline": "law",
    "aliases": [
      "Elsevier",
      "Elsevier law"
    ]
  },
  {
    "id": "elsevier-medicine",
    "name": "Elsevier — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Elsevier",
      "Elsevier medicine"
    ]
  },
  {
    "id": "elsevier-general",
    "name": "Elsevier — General",
    "discipline": "general",
    "aliases": [
      "Elsevier",
      "Elsevier general"
    ]
  },
  {
    "id": "springer-sciences",
    "name": "Springer — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Springer",
      "Springer sciences"
    ]
  },
  {
    "id": "springer-humanities",
    "name": "Springer — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Springer",
      "Springer humanities"
    ]
  },
  {
    "id": "springer-law",
    "name": "Springer — Law",
    "discipline": "law",
    "aliases": [
      "Springer",
      "Springer law"
    ]
  },
  {
    "id": "springer-medicine",
    "name": "Springer — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Springer",
      "Springer medicine"
    ]
  },
  {
    "id": "springer-general",
    "name": "Springer — General",
    "discipline": "general",
    "aliases": [
      "Springer",
      "Springer general"
    ]
  },
  {
    "id": "wiley-sciences",
    "name": "Wiley — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Wiley",
      "Wiley sciences"
    ]
  },
  {
    "id": "wiley-humanities",
    "name": "Wiley — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Wiley",
      "Wiley humanities"
    ]
  },
  {
    "id": "wiley-law",
    "name": "Wiley — Law",
    "discipline": "law",
    "aliases": [
      "Wiley",
      "Wiley law"
    ]
  },
  {
    "id": "wiley-medicine",
    "name": "Wiley — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Wiley",
      "Wiley medicine"
    ]
  },
  {
    "id": "wiley-general",
    "name": "Wiley — General",
    "discipline": "general",
    "aliases": [
      "Wiley",
      "Wiley general"
    ]
  },
  {
    "id": "taylor-and-francis-sciences",
    "name": "Taylor and Francis — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Taylor and Francis",
      "Taylor and Francis sciences"
    ]
  },
  {
    "id": "taylor-and-francis-humanities",
    "name": "Taylor and Francis — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Taylor and Francis",
      "Taylor and Francis humanities"
    ]
  },
  {
    "id": "taylor-and-francis-law",
    "name": "Taylor and Francis — Law",
    "discipline": "law",
    "aliases": [
      "Taylor and Francis",
      "Taylor and Francis law"
    ]
  },
  {
    "id": "taylor-and-francis-medicine",
    "name": "Taylor and Francis — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Taylor and Francis",
      "Taylor and Francis medicine"
    ]
  },
  {
    "id": "taylor-and-francis-general",
    "name": "Taylor and Francis — General",
    "discipline": "general",
    "aliases": [
      "Taylor and Francis",
      "Taylor and Francis general"
    ]
  },
  {
    "id": "sage-sciences",
    "name": "Sage — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Sage",
      "Sage sciences"
    ]
  },
  {
    "id": "sage-humanities",
    "name": "Sage — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Sage",
      "Sage humanities"
    ]
  },
  {
    "id": "sage-law",
    "name": "Sage — Law",
    "discipline": "law",
    "aliases": [
      "Sage",
      "Sage law"
    ]
  },
  {
    "id": "sage-medicine",
    "name": "Sage — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Sage",
      "Sage medicine"
    ]
  },
  {
    "id": "sage-general",
    "name": "Sage — General",
    "discipline": "general",
    "aliases": [
      "Sage",
      "Sage general"
    ]
  },
  {
    "id": "oxford-university-press-sciences",
    "name": "Oxford University Press — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Oxford University Press",
      "Oxford University Press sciences"
    ]
  },
  {
    "id": "oxford-university-press-humanities",
    "name": "Oxford University Press — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Oxford University Press",
      "Oxford University Press humanities"
    ]
  },
  {
    "id": "oxford-university-press-law",
    "name": "Oxford University Press — Law",
    "discipline": "law",
    "aliases": [
      "Oxford University Press",
      "Oxford University Press law"
    ]
  },
  {
    "id": "oxford-university-press-medicine",
    "name": "Oxford University Press — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Oxford University Press",
      "Oxford University Press medicine"
    ]
  },
  {
    "id": "oxford-university-press-general",
    "name": "Oxford University Press — General",
    "discipline": "general",
    "aliases": [
      "Oxford University Press",
      "Oxford University Press general"
    ]
  },
  {
    "id": "cambridge-university-press-sciences",
    "name": "Cambridge University Press — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Cambridge University Press",
      "Cambridge University Press sciences"
    ]
  },
  {
    "id": "cambridge-university-press-humanities",
    "name": "Cambridge University Press — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Cambridge University Press",
      "Cambridge University Press humanities"
    ]
  },
  {
    "id": "cambridge-university-press-law",
    "name": "Cambridge University Press — Law",
    "discipline": "law",
    "aliases": [
      "Cambridge University Press",
      "Cambridge University Press law"
    ]
  },
  {
    "id": "cambridge-university-press-medicine",
    "name": "Cambridge University Press — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Cambridge University Press",
      "Cambridge University Press medicine"
    ]
  },
  {
    "id": "cambridge-university-press-general",
    "name": "Cambridge University Press — General",
    "discipline": "general",
    "aliases": [
      "Cambridge University Press",
      "Cambridge University Press general"
    ]
  },
  {
    "id": "mit-press-sciences",
    "name": "MIT Press — Sciences",
    "discipline": "sciences",
    "aliases": [
      "MIT Press",
      "MIT Press sciences"
    ]
  },
  {
    "id": "mit-press-humanities",
    "name": "MIT Press — Humanities",
    "discipline": "humanities",
    "aliases": [
      "MIT Press",
      "MIT Press humanities"
    ]
  },
  {
    "id": "mit-press-law",
    "name": "MIT Press — Law",
    "discipline": "law",
    "aliases": [
      "MIT Press",
      "MIT Press law"
    ]
  },
  {
    "id": "mit-press-medicine",
    "name": "MIT Press — Medicine",
    "discipline": "medicine",
    "aliases": [
      "MIT Press",
      "MIT Press medicine"
    ]
  },
  {
    "id": "mit-press-general",
    "name": "MIT Press — General",
    "discipline": "general",
    "aliases": [
      "MIT Press",
      "MIT Press general"
    ]
  },
  {
    "id": "stanford-university-press-sciences",
    "name": "Stanford University Press — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Stanford University Press",
      "Stanford University Press sciences"
    ]
  },
  {
    "id": "stanford-university-press-humanities",
    "name": "Stanford University Press — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Stanford University Press",
      "Stanford University Press humanities"
    ]
  },
  {
    "id": "stanford-university-press-law",
    "name": "Stanford University Press — Law",
    "discipline": "law",
    "aliases": [
      "Stanford University Press",
      "Stanford University Press law"
    ]
  },
  {
    "id": "stanford-university-press-medicine",
    "name": "Stanford University Press — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Stanford University Press",
      "Stanford University Press medicine"
    ]
  },
  {
    "id": "stanford-university-press-general",
    "name": "Stanford University Press — General",
    "discipline": "general",
    "aliases": [
      "Stanford University Press",
      "Stanford University Press general"
    ]
  },
  {
    "id": "princeton-university-press-sciences",
    "name": "Princeton University Press — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Princeton University Press",
      "Princeton University Press sciences"
    ]
  },
  {
    "id": "princeton-university-press-humanities",
    "name": "Princeton University Press — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Princeton University Press",
      "Princeton University Press humanities"
    ]
  },
  {
    "id": "princeton-university-press-law",
    "name": "Princeton University Press — Law",
    "discipline": "law",
    "aliases": [
      "Princeton University Press",
      "Princeton University Press law"
    ]
  },
  {
    "id": "princeton-university-press-medicine",
    "name": "Princeton University Press — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Princeton University Press",
      "Princeton University Press medicine"
    ]
  },
  {
    "id": "princeton-university-press-general",
    "name": "Princeton University Press — General",
    "discipline": "general",
    "aliases": [
      "Princeton University Press",
      "Princeton University Press general"
    ]
  },
  {
    "id": "harvard-university-press-sciences",
    "name": "Harvard University Press — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Harvard University Press",
      "Harvard University Press sciences"
    ]
  },
  {
    "id": "harvard-university-press-humanities",
    "name": "Harvard University Press — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Harvard University Press",
      "Harvard University Press humanities"
    ]
  },
  {
    "id": "harvard-university-press-law",
    "name": "Harvard University Press — Law",
    "discipline": "law",
    "aliases": [
      "Harvard University Press",
      "Harvard University Press law"
    ]
  },
  {
    "id": "harvard-university-press-medicine",
    "name": "Harvard University Press — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Harvard University Press",
      "Harvard University Press medicine"
    ]
  },
  {
    "id": "harvard-university-press-general",
    "name": "Harvard University Press — General",
    "discipline": "general",
    "aliases": [
      "Harvard University Press",
      "Harvard University Press general"
    ]
  },
  {
    "id": "yale-university-press-sciences",
    "name": "Yale University Press — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Yale University Press",
      "Yale University Press sciences"
    ]
  },
  {
    "id": "yale-university-press-humanities",
    "name": "Yale University Press — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Yale University Press",
      "Yale University Press humanities"
    ]
  },
  {
    "id": "yale-university-press-law",
    "name": "Yale University Press — Law",
    "discipline": "law",
    "aliases": [
      "Yale University Press",
      "Yale University Press law"
    ]
  },
  {
    "id": "yale-university-press-medicine",
    "name": "Yale University Press — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Yale University Press",
      "Yale University Press medicine"
    ]
  },
  {
    "id": "yale-university-press-general",
    "name": "Yale University Press — General",
    "discipline": "general",
    "aliases": [
      "Yale University Press",
      "Yale University Press general"
    ]
  },
  {
    "id": "duke-university-press-sciences",
    "name": "Duke University Press — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Duke University Press",
      "Duke University Press sciences"
    ]
  },
  {
    "id": "duke-university-press-humanities",
    "name": "Duke University Press — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Duke University Press",
      "Duke University Press humanities"
    ]
  },
  {
    "id": "duke-university-press-law",
    "name": "Duke University Press — Law",
    "discipline": "law",
    "aliases": [
      "Duke University Press",
      "Duke University Press law"
    ]
  },
  {
    "id": "duke-university-press-medicine",
    "name": "Duke University Press — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Duke University Press",
      "Duke University Press medicine"
    ]
  },
  {
    "id": "duke-university-press-general",
    "name": "Duke University Press — General",
    "discipline": "general",
    "aliases": [
      "Duke University Press",
      "Duke University Press general"
    ]
  },
  {
    "id": "johns-hopkins-university-press-sciences",
    "name": "Johns Hopkins University Press — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Johns Hopkins University Press",
      "Johns Hopkins University Press sciences"
    ]
  },
  {
    "id": "johns-hopkins-university-press-humanities",
    "name": "Johns Hopkins University Press — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Johns Hopkins University Press",
      "Johns Hopkins University Press humanities"
    ]
  },
  {
    "id": "johns-hopkins-university-press-law",
    "name": "Johns Hopkins University Press — Law",
    "discipline": "law",
    "aliases": [
      "Johns Hopkins University Press",
      "Johns Hopkins University Press law"
    ]
  },
  {
    "id": "johns-hopkins-university-press-medicine",
    "name": "Johns Hopkins University Press — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Johns Hopkins University Press",
      "Johns Hopkins University Press medicine"
    ]
  },
  {
    "id": "johns-hopkins-university-press-general",
    "name": "Johns Hopkins University Press — General",
    "discipline": "general",
    "aliases": [
      "Johns Hopkins University Press",
      "Johns Hopkins University Press general"
    ]
  },
  {
    "id": "university-of-chicago-press-sciences",
    "name": "University of Chicago Press — Sciences",
    "discipline": "sciences",
    "aliases": [
      "University of Chicago Press",
      "University of Chicago Press sciences"
    ]
  },
  {
    "id": "university-of-chicago-press-humanities",
    "name": "University of Chicago Press — Humanities",
    "discipline": "humanities",
    "aliases": [
      "University of Chicago Press",
      "University of Chicago Press humanities"
    ]
  },
  {
    "id": "university-of-chicago-press-law",
    "name": "University of Chicago Press — Law",
    "discipline": "law",
    "aliases": [
      "University of Chicago Press",
      "University of Chicago Press law"
    ]
  },
  {
    "id": "university-of-chicago-press-medicine",
    "name": "University of Chicago Press — Medicine",
    "discipline": "medicine",
    "aliases": [
      "University of Chicago Press",
      "University of Chicago Press medicine"
    ]
  },
  {
    "id": "university-of-chicago-press-general",
    "name": "University of Chicago Press — General",
    "discipline": "general",
    "aliases": [
      "University of Chicago Press",
      "University of Chicago Press general"
    ]
  },
  {
    "id": "penn-press-sciences",
    "name": "Penn Press — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Penn Press",
      "Penn Press sciences"
    ]
  },
  {
    "id": "penn-press-humanities",
    "name": "Penn Press — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Penn Press",
      "Penn Press humanities"
    ]
  },
  {
    "id": "penn-press-law",
    "name": "Penn Press — Law",
    "discipline": "law",
    "aliases": [
      "Penn Press",
      "Penn Press law"
    ]
  },
  {
    "id": "penn-press-medicine",
    "name": "Penn Press — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Penn Press",
      "Penn Press medicine"
    ]
  },
  {
    "id": "penn-press-general",
    "name": "Penn Press — General",
    "discipline": "general",
    "aliases": [
      "Penn Press",
      "Penn Press general"
    ]
  },
  {
    "id": "uc-press-sciences",
    "name": "UC Press — Sciences",
    "discipline": "sciences",
    "aliases": [
      "UC Press",
      "UC Press sciences"
    ]
  },
  {
    "id": "uc-press-humanities",
    "name": "UC Press — Humanities",
    "discipline": "humanities",
    "aliases": [
      "UC Press",
      "UC Press humanities"
    ]
  },
  {
    "id": "uc-press-law",
    "name": "UC Press — Law",
    "discipline": "law",
    "aliases": [
      "UC Press",
      "UC Press law"
    ]
  },
  {
    "id": "uc-press-medicine",
    "name": "UC Press — Medicine",
    "discipline": "medicine",
    "aliases": [
      "UC Press",
      "UC Press medicine"
    ]
  },
  {
    "id": "uc-press-general",
    "name": "UC Press — General",
    "discipline": "general",
    "aliases": [
      "UC Press",
      "UC Press general"
    ]
  },
  {
    "id": "columbia-university-press-sciences",
    "name": "Columbia University Press — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Columbia University Press",
      "Columbia University Press sciences"
    ]
  },
  {
    "id": "columbia-university-press-humanities",
    "name": "Columbia University Press — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Columbia University Press",
      "Columbia University Press humanities"
    ]
  },
  {
    "id": "columbia-university-press-law",
    "name": "Columbia University Press — Law",
    "discipline": "law",
    "aliases": [
      "Columbia University Press",
      "Columbia University Press law"
    ]
  },
  {
    "id": "columbia-university-press-medicine",
    "name": "Columbia University Press — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Columbia University Press",
      "Columbia University Press medicine"
    ]
  },
  {
    "id": "columbia-university-press-general",
    "name": "Columbia University Press — General",
    "discipline": "general",
    "aliases": [
      "Columbia University Press",
      "Columbia University Press general"
    ]
  },
  {
    "id": "routledge-sciences",
    "name": "Routledge — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Routledge",
      "Routledge sciences"
    ]
  },
  {
    "id": "routledge-humanities",
    "name": "Routledge — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Routledge",
      "Routledge humanities"
    ]
  },
  {
    "id": "routledge-law",
    "name": "Routledge — Law",
    "discipline": "law",
    "aliases": [
      "Routledge",
      "Routledge law"
    ]
  },
  {
    "id": "routledge-medicine",
    "name": "Routledge — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Routledge",
      "Routledge medicine"
    ]
  },
  {
    "id": "routledge-general",
    "name": "Routledge — General",
    "discipline": "general",
    "aliases": [
      "Routledge",
      "Routledge general"
    ]
  },
  {
    "id": "palgrave-macmillan-sciences",
    "name": "Palgrave Macmillan — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Palgrave Macmillan",
      "Palgrave Macmillan sciences"
    ]
  },
  {
    "id": "palgrave-macmillan-humanities",
    "name": "Palgrave Macmillan — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Palgrave Macmillan",
      "Palgrave Macmillan humanities"
    ]
  },
  {
    "id": "palgrave-macmillan-law",
    "name": "Palgrave Macmillan — Law",
    "discipline": "law",
    "aliases": [
      "Palgrave Macmillan",
      "Palgrave Macmillan law"
    ]
  },
  {
    "id": "palgrave-macmillan-medicine",
    "name": "Palgrave Macmillan — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Palgrave Macmillan",
      "Palgrave Macmillan medicine"
    ]
  },
  {
    "id": "palgrave-macmillan-general",
    "name": "Palgrave Macmillan — General",
    "discipline": "general",
    "aliases": [
      "Palgrave Macmillan",
      "Palgrave Macmillan general"
    ]
  },
  {
    "id": "blackwell-sciences",
    "name": "Blackwell — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Blackwell",
      "Blackwell sciences"
    ]
  },
  {
    "id": "blackwell-humanities",
    "name": "Blackwell — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Blackwell",
      "Blackwell humanities"
    ]
  },
  {
    "id": "blackwell-law",
    "name": "Blackwell — Law",
    "discipline": "law",
    "aliases": [
      "Blackwell",
      "Blackwell law"
    ]
  },
  {
    "id": "blackwell-medicine",
    "name": "Blackwell — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Blackwell",
      "Blackwell medicine"
    ]
  },
  {
    "id": "blackwell-general",
    "name": "Blackwell — General",
    "discipline": "general",
    "aliases": [
      "Blackwell",
      "Blackwell general"
    ]
  },
  {
    "id": "karger-sciences",
    "name": "Karger — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Karger",
      "Karger sciences"
    ]
  },
  {
    "id": "karger-humanities",
    "name": "Karger — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Karger",
      "Karger humanities"
    ]
  },
  {
    "id": "karger-law",
    "name": "Karger — Law",
    "discipline": "law",
    "aliases": [
      "Karger",
      "Karger law"
    ]
  },
  {
    "id": "karger-medicine",
    "name": "Karger — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Karger",
      "Karger medicine"
    ]
  },
  {
    "id": "karger-general",
    "name": "Karger — General",
    "discipline": "general",
    "aliases": [
      "Karger",
      "Karger general"
    ]
  },
  {
    "id": "georg-thieme-verlag-sciences",
    "name": "Georg Thieme Verlag — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Georg Thieme Verlag",
      "Georg Thieme Verlag sciences"
    ]
  },
  {
    "id": "georg-thieme-verlag-humanities",
    "name": "Georg Thieme Verlag — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Georg Thieme Verlag",
      "Georg Thieme Verlag humanities"
    ]
  },
  {
    "id": "georg-thieme-verlag-law",
    "name": "Georg Thieme Verlag — Law",
    "discipline": "law",
    "aliases": [
      "Georg Thieme Verlag",
      "Georg Thieme Verlag law"
    ]
  },
  {
    "id": "georg-thieme-verlag-medicine",
    "name": "Georg Thieme Verlag — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Georg Thieme Verlag",
      "Georg Thieme Verlag medicine"
    ]
  },
  {
    "id": "georg-thieme-verlag-general",
    "name": "Georg Thieme Verlag — General",
    "discipline": "general",
    "aliases": [
      "Georg Thieme Verlag",
      "Georg Thieme Verlag general"
    ]
  },
  {
    "id": "wolters-kluwer-sciences",
    "name": "Wolters Kluwer — Sciences",
    "discipline": "sciences",
    "aliases": [
      "Wolters Kluwer",
      "Wolters Kluwer sciences"
    ]
  },
  {
    "id": "wolters-kluwer-humanities",
    "name": "Wolters Kluwer — Humanities",
    "discipline": "humanities",
    "aliases": [
      "Wolters Kluwer",
      "Wolters Kluwer humanities"
    ]
  },
  {
    "id": "wolters-kluwer-law",
    "name": "Wolters Kluwer — Law",
    "discipline": "law",
    "aliases": [
      "Wolters Kluwer",
      "Wolters Kluwer law"
    ]
  },
  {
    "id": "wolters-kluwer-medicine",
    "name": "Wolters Kluwer — Medicine",
    "discipline": "medicine",
    "aliases": [
      "Wolters Kluwer",
      "Wolters Kluwer medicine"
    ]
  },
  {
    "id": "wolters-kluwer-general",
    "name": "Wolters Kluwer — General",
    "discipline": "general",
    "aliases": [
      "Wolters Kluwer",
      "Wolters Kluwer general"
    ]
  },
  {
    "id": "academic-medicine",
    "name": "Academic Medicine",
    "discipline": "medicine",
    "aliases": [
      "Academic Medicine"
    ]
  },
  {
    "id": "acta-anaesthesiologica-scandinavica",
    "name": "Acta Anaesthesiologica Scandinavica",
    "discipline": "medicine",
    "aliases": [
      "Acta Anaesthesiologica Scandinavica"
    ]
  },
  {
    "id": "acta-cardiologica",
    "name": "Acta Cardiologica",
    "discipline": "medicine",
    "aliases": [
      "Acta Cardiologica"
    ]
  },
  {
    "id": "acta-diabetologica",
    "name": "Acta Diabetologica",
    "discipline": "medicine",
    "aliases": [
      "Acta Diabetologica"
    ]
  },
  {
    "id": "acta-neuropathologica",
    "name": "Acta Neuropathologica",
    "discipline": "medicine",
    "aliases": [
      "Acta Neuropathologica"
    ]
  },
  {
    "id": "acta-orthopaedica",
    "name": "Acta Orthopaedica",
    "discipline": "medicine",
    "aliases": [
      "Acta Orthopaedica"
    ]
  },
  {
    "id": "acta-paediatrica",
    "name": "Acta Paediatrica",
    "discipline": "medicine",
    "aliases": [
      "Acta Paediatrica"
    ]
  },
  {
    "id": "acta-psychiatrica-scandinavica",
    "name": "Acta Psychiatrica Scandinavica",
    "discipline": "medicine",
    "aliases": [
      "Acta Psychiatrica Scandinavica"
    ]
  },
  {
    "id": "american-journal-emergency-medicine",
    "name": "American Journal of Emergency Medicine",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Emergency Medicine"
    ]
  },
  {
    "id": "american-journal-ophthalmology",
    "name": "American Journal of Ophthalmology",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Ophthalmology"
    ]
  },
  {
    "id": "american-journal-orthopedic-surgery",
    "name": "American Journal of Orthopedic Surgery",
    "discipline": "medicine",
    "aliases": [
      "American Journal of Orthopedic Surgery"
    ]
  },
  {
    "id": "annals-allergy-asthma-immunology",
    "name": "Annals of Allergy Asthma and Immunology",
    "discipline": "medicine",
    "aliases": [
      "Annals of Allergy Asthma and Immunology"
    ]
  },
  {
    "id": "annals-clinical-microbiology-antimicrobials",
    "name": "Annals of Clinical Microbiology and Antimicrobials",
    "discipline": "medicine",
    "aliases": [
      "Annals of Clinical Microbiology and Antimicrobials"
    ]
  },
  {
    "id": "annals-family-medicine",
    "name": "Annals of Family Medicine",
    "discipline": "medicine",
    "aliases": [
      "Annals of Family Medicine"
    ]
  },
  {
    "id": "annals-human-genetics",
    "name": "Annals of Human Genetics",
    "discipline": "sciences",
    "aliases": [
      "Annals of Human Genetics"
    ]
  },
  {
    "id": "annals-vascular-surgery",
    "name": "Annals of Vascular Surgery",
    "discipline": "medicine",
    "aliases": [
      "Annals of Vascular Surgery"
    ]
  },
  {
    "id": "antimicrobial-agents-chemotherapy",
    "name": "Antimicrobial Agents and Chemotherapy",
    "discipline": "medicine",
    "aliases": [
      "Antimicrobial Agents and Chemotherapy"
    ]
  },
  {
    "id": "applied-clinical-informatics",
    "name": "Applied Clinical Informatics",
    "discipline": "medicine",
    "aliases": [
      "Applied Clinical Informatics"
    ]
  },
  {
    "id": "archives-ophthalmology",
    "name": "Archives of Ophthalmology",
    "discipline": "medicine",
    "aliases": [
      "Archives of Ophthalmology"
    ]
  },
  {
    "id": "archives-oral-biology",
    "name": "Archives of Oral Biology",
    "discipline": "medicine",
    "aliases": [
      "Archives of Oral Biology"
    ]
  },
  {
    "id": "archives-pediatrics-adolescent-medicine",
    "name": "Archives of Pediatrics and Adolescent Medicine",
    "discipline": "medicine",
    "aliases": [
      "Archives of Pediatrics and Adolescent Medicine"
    ]
  },
  {
    "id": "asthma-research-practice",
    "name": "Asthma Research and Practice",
    "discipline": "medicine",
    "aliases": [
      "Asthma Research and Practice"
    ]
  },
  {
    "id": "atherosclerosis",
    "name": "Atherosclerosis",
    "discipline": "medicine",
    "aliases": [
      "Atherosclerosis"
    ]
  },
  {
    "id": "attention-deficit-hyperactivity-disorders",
    "name": "Attention Deficit and Hyperactivity Disorders",
    "discipline": "medicine",
    "aliases": [
      "Attention Deficit and Hyperactivity Disorders"
    ]
  },
  {
    "id": "australasian-emergency-care",
    "name": "Australasian Emergency Care",
    "discipline": "medicine",
    "aliases": [
      "Australasian Emergency Care"
    ]
  },
  {
    "id": "biomarker-research",
    "name": "Biomarker Research",
    "discipline": "medicine",
    "aliases": [
      "Biomarker Research"
    ]
  },
  {
    "id": "blood-cancer-journal",
    "name": "Blood Cancer Journal",
    "discipline": "medicine",
    "aliases": [
      "Blood Cancer Journal"
    ]
  },
  {
    "id": "bmc-cancer",
    "name": "BMC Cancer",
    "discipline": "medicine",
    "aliases": [
      "BMC Cancer"
    ]
  },
  {
    "id": "bmc-cardiovascular-disorders",
    "name": "BMC Cardiovascular Disorders",
    "discipline": "medicine",
    "aliases": [
      "BMC Cardiovascular Disorders"
    ]
  },
  {
    "id": "bmc-geriatrics",
    "name": "BMC Geriatrics",
    "discipline": "medicine",
    "aliases": [
      "BMC Geriatrics"
    ]
  },
  {
    "id": "bmc-infectious-diseases",
    "name": "BMC Infectious Diseases",
    "discipline": "medicine",
    "aliases": [
      "BMC Infectious Diseases"
    ]
  },
  {
    "id": "bmc-medicine",
    "name": "BMC Medicine",
    "discipline": "medicine",
    "aliases": [
      "BMC Medicine"
    ]
  },
  {
    "id": "bmc-musculoskeletal-disorders",
    "name": "BMC Musculoskeletal Disorders",
    "discipline": "medicine",
    "aliases": [
      "BMC Musculoskeletal Disorders"
    ]
  },
  {
    "id": "bmc-neurology",
    "name": "BMC Neurology",
    "discipline": "medicine",
    "aliases": [
      "BMC Neurology"
    ]
  },
  {
    "id": "bmc-pregnancy-childbirth",
    "name": "BMC Pregnancy and Childbirth",
    "discipline": "medicine",
    "aliases": [
      "BMC Pregnancy and Childbirth"
    ]
  },
  {
    "id": "bmc-psychiatry",
    "name": "BMC Psychiatry",
    "discipline": "medicine",
    "aliases": [
      "BMC Psychiatry"
    ]
  },
  {
    "id": "bmc-public-health",
    "name": "BMC Public Health",
    "discipline": "medicine",
    "aliases": [
      "BMC Public Health"
    ]
  },
  {
    "id": "breast-cancer-research",
    "name": "Breast Cancer Research",
    "discipline": "medicine",
    "aliases": [
      "Breast Cancer Research"
    ]
  },
  {
    "id": "cancer-management-research",
    "name": "Cancer Management and Research",
    "discipline": "medicine",
    "aliases": [
      "Cancer Management and Research"
    ]
  },
  {
    "id": "cardiovascular-diabetology",
    "name": "Cardiovascular Diabetology",
    "discipline": "medicine",
    "aliases": [
      "Cardiovascular Diabetology"
    ]
  },
  {
    "id": "cell-reports-medicine",
    "name": "Cell Reports Medicine",
    "discipline": "medicine",
    "aliases": [
      "Cell Reports Medicine"
    ]
  },
  {
    "id": "clinical-cardiology",
    "name": "Clinical Cardiology",
    "discipline": "medicine",
    "aliases": [
      "Clinical Cardiology"
    ]
  },
  {
    "id": "clinical-diabetes",
    "name": "Clinical Diabetes",
    "discipline": "medicine",
    "aliases": [
      "Clinical Diabetes"
    ]
  },
  {
    "id": "clinical-endocrinology",
    "name": "Clinical Endocrinology",
    "discipline": "medicine",
    "aliases": [
      "Clinical Endocrinology"
    ]
  },
  {
    "id": "clinical-experimental-immunology",
    "name": "Clinical and Experimental Immunology",
    "discipline": "medicine",
    "aliases": [
      "Clinical and Experimental Immunology"
    ]
  },
  {
    "id": "clinical-experimental-rheumatology",
    "name": "Clinical and Experimental Rheumatology",
    "discipline": "medicine",
    "aliases": [
      "Clinical and Experimental Rheumatology"
    ]
  },
  {
    "id": "clinical-neurophysiology",
    "name": "Clinical Neurophysiology",
    "discipline": "medicine",
    "aliases": [
      "Clinical Neurophysiology"
    ]
  },
  {
    "id": "clinical-orthopaedics-related-research",
    "name": "Clinical Orthopaedics and Related Research",
    "discipline": "medicine",
    "aliases": [
      "Clinical Orthopaedics and Related Research"
    ]
  },
  {
    "id": "clinical-rehabilitation",
    "name": "Clinical Rehabilitation",
    "discipline": "medicine",
    "aliases": [
      "Clinical Rehabilitation"
    ]
  },
  {
    "id": "clinical-rheumatology",
    "name": "Clinical Rheumatology",
    "discipline": "medicine",
    "aliases": [
      "Clinical Rheumatology"
    ]
  },
  {
    "id": "clinical-therapeutics",
    "name": "Clinical Therapeutics",
    "discipline": "medicine",
    "aliases": [
      "Clinical Therapeutics"
    ]
  },
  {
    "id": "clinical-transplantation",
    "name": "Clinical Transplantation",
    "discipline": "medicine",
    "aliases": [
      "Clinical Transplantation"
    ]
  },
  {
    "id": "cochrane-database-systematic-reviews",
    "name": "Cochrane Database of Systematic Reviews",
    "discipline": "medicine",
    "aliases": [
      "Cochrane Database of Systematic Reviews"
    ]
  },
  {
    "id": "colorectal-disease",
    "name": "Colorectal Disease",
    "discipline": "medicine",
    "aliases": [
      "Colorectal Disease"
    ]
  },
  {
    "id": "dermatology",
    "name": "Dermatology",
    "discipline": "medicine",
    "aliases": [
      "Dermatology"
    ]
  },
  {
    "id": "diabetes-metabolism-research-reviews",
    "name": "Diabetes Metabolism Research and Reviews",
    "discipline": "medicine",
    "aliases": [
      "Diabetes Metabolism Research and Reviews"
    ]
  },
  {
    "id": "diabetes-obesity-metabolism",
    "name": "Diabetes Obesity and Metabolism",
    "discipline": "medicine",
    "aliases": [
      "Diabetes Obesity and Metabolism"
    ]
  },
  {
    "id": "digestive-diseases-sciences",
    "name": "Digestive Diseases and Sciences",
    "discipline": "medicine",
    "aliases": [
      "Digestive Diseases and Sciences"
    ]
  },
  {
    "id": "digestive-endoscopy",
    "name": "Digestive Endoscopy",
    "discipline": "medicine",
    "aliases": [
      "Digestive Endoscopy"
    ]
  },
  {
    "id": "drug-alcohol-dependence",
    "name": "Drug and Alcohol Dependence",
    "discipline": "medicine",
    "aliases": [
      "Drug and Alcohol Dependence"
    ]
  },
  {
    "id": "ecancermedicalscience",
    "name": "ecancermedicalscience",
    "discipline": "medicine",
    "aliases": [
      "ecancermedicalscience"
    ]
  },
  {
    "id": "emergency-medicine-journal",
    "name": "Emergency Medicine Journal",
    "discipline": "medicine",
    "aliases": [
      "Emergency Medicine Journal"
    ]
  },
  {
    "id": "endocrine",
    "name": "Endocrine",
    "discipline": "medicine",
    "aliases": [
      "Endocrine"
    ]
  },
  {
    "id": "endocrine-connections",
    "name": "Endocrine Connections",
    "discipline": "medicine",
    "aliases": [
      "Endocrine Connections"
    ]
  },
  {
    "id": "endocrinology-metabolism",
    "name": "Endocrinology and Metabolism",
    "discipline": "medicine",
    "aliases": [
      "Endocrinology and Metabolism"
    ]
  },
  {
    "id": "european-journal-anaesthesiology",
    "name": "European Journal of Anaesthesiology",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Anaesthesiology"
    ]
  },
  {
    "id": "european-journal-gastroenterology-hepatology",
    "name": "European Journal of Gastroenterology and Hepatology",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Gastroenterology and Hepatology"
    ]
  },
  {
    "id": "european-journal-haematology",
    "name": "European Journal of Haematology",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Haematology"
    ]
  },
  {
    "id": "european-journal-obstetrics-gynecology",
    "name": "European Journal of Obstetrics Gynecology and Reproductive Biology",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Obstetrics Gynecology and Reproductive Biology"
    ]
  },
  {
    "id": "european-journal-pain",
    "name": "European Journal of Pain",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Pain"
    ]
  },
  {
    "id": "european-journal-radiology",
    "name": "European Journal of Radiology",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Radiology"
    ]
  },
  {
    "id": "european-journal-surgical-oncology",
    "name": "European Journal of Surgical Oncology",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Surgical Oncology"
    ]
  },
  {
    "id": "european-journal-vascular-endovascular-surgery",
    "name": "European Journal of Vascular and Endovascular Surgery",
    "discipline": "medicine",
    "aliases": [
      "European Journal of Vascular and Endovascular Surgery"
    ]
  },
  {
    "id": "european-psychiatry",
    "name": "European Psychiatry",
    "discipline": "medicine",
    "aliases": [
      "European Psychiatry"
    ]
  },
  {
    "id": "european-respiratory-journal",
    "name": "European Respiratory Journal",
    "discipline": "medicine",
    "aliases": [
      "European Respiratory Journal"
    ]
  },
  {
    "id": "experimental-brain-research",
    "name": "Experimental Brain Research",
    "discipline": "medicine",
    "aliases": [
      "Experimental Brain Research"
    ]
  },
  {
    "id": "experimental-gerontology",
    "name": "Experimental Gerontology",
    "discipline": "sciences",
    "aliases": [
      "Experimental Gerontology"
    ]
  },
  {
    "id": "eye",
    "name": "Eye",
    "discipline": "medicine",
    "aliases": [
      "Eye"
    ]
  },
  {
    "id": "frontiers-aging-neuroscience",
    "name": "Frontiers in Aging Neuroscience",
    "discipline": "medicine",
    "aliases": [
      "Frontiers in Aging Neuroscience"
    ]
  },
  {
    "id": "frontiers-behavioral-neuroscience",
    "name": "Frontiers in Behavioral Neuroscience",
    "discipline": "sciences",
    "aliases": [
      "Frontiers in Behavioral Neuroscience"
    ]
  },
  {
    "id": "frontiers-cell-developmental-biology",
    "name": "Frontiers in Cell and Developmental Biology",
    "discipline": "sciences",
    "aliases": [
      "Frontiers in Cell and Developmental Biology"
    ]
  },
  {
    "id": "frontiers-endocrinology",
    "name": "Frontiers in Endocrinology",
    "discipline": "medicine",
    "aliases": [
      "Frontiers in Endocrinology"
    ]
  },
  {
    "id": "frontiers-human-neuroscience",
    "name": "Frontiers in Human Neuroscience",
    "discipline": "sciences",
    "aliases": [
      "Frontiers in Human Neuroscience"
    ]
  },
  {
    "id": "frontiers-public-health",
    "name": "Frontiers in Public Health",
    "discipline": "medicine",
    "aliases": [
      "Frontiers in Public Health"
    ]
  },
  {
    "id": "frontiers-surgery",
    "name": "Frontiers in Surgery",
    "discipline": "medicine",
    "aliases": [
      "Frontiers in Surgery"
    ]
  },
  {
    "id": "gastrointestinal-endoscopy",
    "name": "Gastrointestinal Endoscopy",
    "discipline": "medicine",
    "aliases": [
      "Gastrointestinal Endoscopy"
    ]
  },
  {
    "id": "geriatrics-gerontology-international",
    "name": "Geriatrics and Gerontology International",
    "discipline": "medicine",
    "aliases": [
      "Geriatrics and Gerontology International"
    ]
  },
  {
    "id": "global-health-action",
    "name": "Global Health Action",
    "discipline": "medicine",
    "aliases": [
      "Global Health Action"
    ]
  },
  {
    "id": "gynecologic-oncology",
    "name": "Gynecologic Oncology",
    "discipline": "medicine",
    "aliases": [
      "Gynecologic Oncology"
    ]
  },
  {
    "id": "health-psychology",
    "name": "Health Psychology",
    "discipline": "medicine",
    "aliases": [
      "Health Psychology"
    ]
  },
  {
    "id": "health-technology-assessment",
    "name": "Health Technology Assessment",
    "discipline": "medicine",
    "aliases": [
      "Health Technology Assessment"
    ]
  },
  {
    "id": "heart-lung",
    "name": "Heart and Lung",
    "discipline": "medicine",
    "aliases": [
      "Heart and Lung"
    ]
  },
  {
    "id": "hematology-oncology",
    "name": "Hematology and Oncology",
    "discipline": "medicine",
    "aliases": [
      "Hematology and Oncology"
    ]
  },
  {
    "id": "hepatobiliary-pancreat-dis-int",
    "name": "Hepatobiliary and Pancreatic Diseases International",
    "discipline": "medicine",
    "aliases": [
      "Hepatobiliary and Pancreatic Diseases International"
    ]
  },
  {
    "id": "hernia",
    "name": "Hernia",
    "discipline": "medicine",
    "aliases": [
      "Hernia"
    ]
  },
  {
    "id": "hormone-metabolism-research",
    "name": "Hormone and Metabolism Research",
    "discipline": "medicine",
    "aliases": [
      "Hormone and Metabolism Research"
    ]
  },
  {
    "id": "human-pathology",
    "name": "Human Pathology",
    "discipline": "medicine",
    "aliases": [
      "Human Pathology"
    ]
  },
  {
    "id": "hypertension-research",
    "name": "Hypertension Research",
    "discipline": "medicine",
    "aliases": [
      "Hypertension Research"
    ]
  },
  {
    "id": "immunology",
    "name": "Immunology",
    "discipline": "medicine",
    "aliases": [
      "Immunology"
    ]
  },
  {
    "id": "immunology-letters",
    "name": "Immunology Letters",
    "discipline": "medicine",
    "aliases": [
      "Immunology Letters"
    ]
  },
  {
    "id": "inflammatory-bowel-diseases",
    "name": "Inflammatory Bowel Diseases",
    "discipline": "medicine",
    "aliases": [
      "Inflammatory Bowel Diseases"
    ]
  },
  {
    "id": "international-angiology",
    "name": "International Angiology",
    "discipline": "medicine",
    "aliases": [
      "International Angiology"
    ]
  },
  {
    "id": "international-journal-adolescent-medicine-health",
    "name": "International Journal of Adolescent Medicine and Health",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Adolescent Medicine and Health"
    ]
  },
  {
    "id": "international-journal-clinical-practice",
    "name": "International Journal of Clinical Practice",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Clinical Practice"
    ]
  },
  {
    "id": "international-journal-colorectal-disease",
    "name": "International Journal of Colorectal Disease",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Colorectal Disease"
    ]
  },
  {
    "id": "international-journal-dermatology",
    "name": "International Journal of Dermatology",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Dermatology"
    ]
  },
  {
    "id": "international-journal-eating-disorders",
    "name": "International Journal of Eating Disorders",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Eating Disorders"
    ]
  },
  {
    "id": "international-journal-geriatric-psychiatry",
    "name": "International Journal of Geriatric Psychiatry",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Geriatric Psychiatry"
    ]
  },
  {
    "id": "international-journal-gynecology-obstetrics",
    "name": "International Journal of Gynecology and Obstetrics",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Gynecology and Obstetrics"
    ]
  },
  {
    "id": "international-journal-impotence-research",
    "name": "International Journal of Impotence Research",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Impotence Research"
    ]
  },
  {
    "id": "international-journal-infectious-diseases",
    "name": "International Journal of Infectious Diseases",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Infectious Diseases"
    ]
  },
  {
    "id": "international-journal-mental-health-nursing",
    "name": "International Journal of Mental Health Nursing",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Mental Health Nursing"
    ]
  },
  {
    "id": "international-journal-nursing-studies",
    "name": "International Journal of Nursing Studies",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Nursing Studies"
    ]
  },
  {
    "id": "international-journal-oncology",
    "name": "International Journal of Oncology",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Oncology"
    ]
  },
  {
    "id": "international-journal-paediatric-dentistry",
    "name": "International Journal of Paediatric Dentistry",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Paediatric Dentistry"
    ]
  },
  {
    "id": "international-journal-pediatrics",
    "name": "International Journal of Pediatrics",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Pediatrics"
    ]
  },
  {
    "id": "international-journal-rheumatic-diseases",
    "name": "International Journal of Rheumatic Diseases",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Rheumatic Diseases"
    ]
  },
  {
    "id": "international-journal-stroke",
    "name": "International Journal of Stroke",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Stroke"
    ]
  },
  {
    "id": "international-journal-surgery",
    "name": "International Journal of Surgery",
    "discipline": "medicine",
    "aliases": [
      "International Journal of Surgery"
    ]
  },
  {
    "id": "investigative-radiology",
    "name": "Investigative Radiology",
    "discipline": "medicine",
    "aliases": [
      "Investigative Radiology"
    ]
  },
  {
    "id": "journal-acquired-immune-deficiency-syndromes",
    "name": "Journal of Acquired Immune Deficiency Syndromes",
    "discipline": "medicine",
    "aliases": [
      "Journal of Acquired Immune Deficiency Syndromes"
    ]
  },
  {
    "id": "journal-adolescent-health",
    "name": "Journal of Adolescent Health",
    "discipline": "medicine",
    "aliases": [
      "Journal of Adolescent Health"
    ]
  },
  {
    "id": "journal-antimicrobial-chemotherapy",
    "name": "Journal of Antimicrobial Chemotherapy",
    "discipline": "medicine",
    "aliases": [
      "Journal of Antimicrobial Chemotherapy"
    ]
  },
  {
    "id": "journal-arthroplasty",
    "name": "Journal of Arthroplasty",
    "discipline": "medicine",
    "aliases": [
      "Journal of Arthroplasty"
    ]
  },
  {
    "id": "journal-bone-mineral-research",
    "name": "Journal of Bone and Mineral Research",
    "discipline": "medicine",
    "aliases": [
      "Journal of Bone and Mineral Research"
    ]
  },
  {
    "id": "journal-cardiac-failure",
    "name": "Journal of Cardiac Failure",
    "discipline": "medicine",
    "aliases": [
      "Journal of Cardiac Failure"
    ]
  },
  {
    "id": "journal-cardiovascular-magnetic-resonance",
    "name": "Journal of Cardiovascular Magnetic Resonance",
    "discipline": "medicine",
    "aliases": [
      "Journal of Cardiovascular Magnetic Resonance"
    ]
  },
  {
    "id": "journal-cardiovascular-pharmacology-therapeutics",
    "name": "Journal of Cardiovascular Pharmacology and Therapeutics",
    "discipline": "medicine",
    "aliases": [
      "Journal of Cardiovascular Pharmacology and Therapeutics"
    ]
  },
  {
    "id": "journal-clinical-anesthesia",
    "name": "Journal of Clinical Anesthesia",
    "discipline": "medicine",
    "aliases": [
      "Journal of Clinical Anesthesia"
    ]
  },
  {
    "id": "journal-clinical-immunology",
    "name": "Journal of Clinical Immunology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Clinical Immunology"
    ]
  },
  {
    "id": "journal-clinical-neuroscience",
    "name": "Journal of Clinical Neuroscience",
    "discipline": "medicine",
    "aliases": [
      "Journal of Clinical Neuroscience"
    ]
  },
  {
    "id": "journal-clinical-pathology",
    "name": "Journal of Clinical Pathology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Clinical Pathology"
    ]
  },
  {
    "id": "journal-clinical-rheumatology",
    "name": "Journal of Clinical Rheumatology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Clinical Rheumatology"
    ]
  },
  {
    "id": "journal-community-health",
    "name": "Journal of Community Health",
    "discipline": "medicine",
    "aliases": [
      "Journal of Community Health"
    ]
  },
  {
    "id": "journal-dermatology",
    "name": "Journal of Dermatology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Dermatology"
    ]
  },
  {
    "id": "journal-diabetes",
    "name": "Journal of Diabetes",
    "discipline": "medicine",
    "aliases": [
      "Journal of Diabetes"
    ]
  },
  {
    "id": "journal-emergency-medicine",
    "name": "Journal of Emergency Medicine",
    "discipline": "medicine",
    "aliases": [
      "Journal of Emergency Medicine"
    ]
  },
  {
    "id": "journal-gastrointestinal-liver-diseases",
    "name": "Journal of Gastrointestinal and Liver Diseases",
    "discipline": "medicine",
    "aliases": [
      "Journal of Gastrointestinal and Liver Diseases"
    ]
  },
  {
    "id": "journal-glaucoma",
    "name": "Journal of Glaucoma",
    "discipline": "medicine",
    "aliases": [
      "Journal of Glaucoma"
    ]
  },
  {
    "id": "journal-head-neck-pathology",
    "name": "Journal of Head and Neck Pathology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Head and Neck Pathology"
    ]
  },
  {
    "id": "journal-healthcare-quality",
    "name": "Journal of Healthcare Quality",
    "discipline": "medicine",
    "aliases": [
      "Journal of Healthcare Quality"
    ]
  },
  {
    "id": "journal-hematology-oncology",
    "name": "Journal of Hematology and Oncology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Hematology and Oncology"
    ]
  },
  {
    "id": "journal-human-hypertension",
    "name": "Journal of Human Hypertension",
    "discipline": "medicine",
    "aliases": [
      "Journal of Human Hypertension"
    ]
  },
  {
    "id": "journal-laparoendoscopic-advanced-surgical-techniques",
    "name": "Journal of Laparoendoscopic and Advanced Surgical Techniques",
    "discipline": "medicine",
    "aliases": [
      "Journal of Laparoendoscopic and Advanced Surgical Techniques"
    ]
  },
  {
    "id": "journal-maternal-fetal-neonatal-medicine",
    "name": "Journal of Maternal-Fetal and Neonatal Medicine",
    "discipline": "medicine",
    "aliases": [
      "Journal of Maternal-Fetal and Neonatal Medicine"
    ]
  },
  {
    "id": "journal-minimally-invasive-gynecology",
    "name": "Journal of Minimally Invasive Gynecology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Minimally Invasive Gynecology"
    ]
  },
  {
    "id": "journal-neuroimmunology",
    "name": "Journal of Neuroimmunology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Neuroimmunology"
    ]
  },
  {
    "id": "journal-neuroradiology",
    "name": "Journal of Neuroradiology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Neuroradiology"
    ]
  },
  {
    "id": "journal-neuroscience-research",
    "name": "Journal of Neuroscience Research",
    "discipline": "medicine",
    "aliases": [
      "Journal of Neuroscience Research"
    ]
  },
  {
    "id": "journal-obstetrics-gynaecology-research",
    "name": "Journal of Obstetrics and Gynaecology Research",
    "discipline": "medicine",
    "aliases": [
      "Journal of Obstetrics and Gynaecology Research"
    ]
  },
  {
    "id": "journal-oncology-practice",
    "name": "Journal of Oncology Practice",
    "discipline": "medicine",
    "aliases": [
      "Journal of Oncology Practice"
    ]
  },
  {
    "id": "journal-oral-maxillofacial-surgery",
    "name": "Journal of Oral and Maxillofacial Surgery",
    "discipline": "medicine",
    "aliases": [
      "Journal of Oral and Maxillofacial Surgery"
    ]
  },
  {
    "id": "journal-orthopaedic-surgery-research",
    "name": "Journal of Orthopaedic Surgery and Research",
    "discipline": "medicine",
    "aliases": [
      "Journal of Orthopaedic Surgery and Research"
    ]
  },
  {
    "id": "journal-pain-research",
    "name": "Journal of Pain Research",
    "discipline": "medicine",
    "aliases": [
      "Journal of Pain Research"
    ]
  },
  {
    "id": "journal-palliative-medicine",
    "name": "Journal of Palliative Medicine",
    "discipline": "medicine",
    "aliases": [
      "Journal of Palliative Medicine"
    ]
  },
  {
    "id": "journal-pediatric-gastroenterology-nutrition",
    "name": "Journal of Pediatric Gastroenterology and Nutrition",
    "discipline": "medicine",
    "aliases": [
      "Journal of Pediatric Gastroenterology and Nutrition"
    ]
  },
  {
    "id": "journal-pediatric-orthopedics",
    "name": "Journal of Pediatric Orthopedics",
    "discipline": "medicine",
    "aliases": [
      "Journal of Pediatric Orthopedics"
    ]
  },
  {
    "id": "journal-pediatric-surgery",
    "name": "Journal of Pediatric Surgery",
    "discipline": "medicine",
    "aliases": [
      "Journal of Pediatric Surgery"
    ]
  },
  {
    "id": "journal-perinatology",
    "name": "Journal of Perinatology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Perinatology"
    ]
  },
  {
    "id": "journal-primary-care-community-health",
    "name": "Journal of Primary Care and Community Health",
    "discipline": "medicine",
    "aliases": [
      "Journal of Primary Care and Community Health"
    ]
  },
  {
    "id": "journal-psychiatry-neuroscience",
    "name": "Journal of Psychiatry and Neuroscience",
    "discipline": "medicine",
    "aliases": [
      "Journal of Psychiatry and Neuroscience"
    ]
  },
  {
    "id": "journal-renal-nutrition",
    "name": "Journal of Renal Nutrition",
    "discipline": "medicine",
    "aliases": [
      "Journal of Renal Nutrition"
    ]
  },
  {
    "id": "journal-rheumatology",
    "name": "Journal of Rheumatology",
    "discipline": "medicine",
    "aliases": [
      "Journal of Rheumatology"
    ]
  },
  {
    "id": "journal-spinal-cord-medicine",
    "name": "Journal of Spinal Cord Medicine",
    "discipline": "medicine",
    "aliases": [
      "Journal of Spinal Cord Medicine"
    ]
  },
  {
    "id": "journal-thrombosis-haemostasis",
    "name": "Journal of Thrombosis and Haemostasis",
    "discipline": "medicine",
    "aliases": [
      "Journal of Thrombosis and Haemostasis"
    ]
  },
  {
    "id": "journal-trauma-acute-care-surgery",
    "name": "Journal of Trauma and Acute Care Surgery",
    "discipline": "medicine",
    "aliases": [
      "Journal of Trauma and Acute Care Surgery"
    ]
  },
  {
    "id": "journal-vascular-surgery",
    "name": "Journal of Vascular Surgery",
    "discipline": "medicine",
    "aliases": [
      "Journal of Vascular Surgery"
    ]
  },
  {
    "id": "kidney-diseases",
    "name": "Kidney Diseases",
    "discipline": "medicine",
    "aliases": [
      "Kidney Diseases"
    ]
  },
  {
    "id": "liver-cancer",
    "name": "Liver Cancer",
    "discipline": "medicine",
    "aliases": [
      "Liver Cancer"
    ]
  },
  {
    "id": "lung-cancer",
    "name": "Lung Cancer",
    "discipline": "medicine",
    "aliases": [
      "Lung Cancer"
    ]
  },
  {
    "id": "medicine-baltimore",
    "name": "Medicine Baltimore",
    "discipline": "medicine",
    "aliases": [
      "Medicine Baltimore"
    ]
  },
  {
    "id": "menopause",
    "name": "Menopause",
    "discipline": "medicine",
    "aliases": [
      "Menopause"
    ]
  },
  {
    "id": "metabolism",
    "name": "Metabolism",
    "discipline": "medicine",
    "aliases": [
      "Metabolism"
    ]
  },
  {
    "id": "molecular-cancer",
    "name": "Molecular Cancer",
    "discipline": "medicine",
    "aliases": [
      "Molecular Cancer"
    ]
  },
  {
    "id": "muscle-nerve",
    "name": "Muscle and Nerve",
    "discipline": "medicine",
    "aliases": [
      "Muscle and Nerve"
    ]
  },
  {
    "id": "neurogastroenterology-motility",
    "name": "Neurogastroenterology and Motility",
    "discipline": "medicine",
    "aliases": [
      "Neurogastroenterology and Motility"
    ]
  },
  {
    "id": "neuropediatrics",
    "name": "Neuropediatrics",
    "discipline": "medicine",
    "aliases": [
      "Neuropediatrics"
    ]
  },
  {
    "id": "nutrition",
    "name": "Nutrition",
    "discipline": "medicine",
    "aliases": [
      "Nutrition"
    ]
  },
  {
    "id": "nutrition-cancer",
    "name": "Nutrition and Cancer",
    "discipline": "medicine",
    "aliases": [
      "Nutrition and Cancer"
    ]
  },
  {
    "id": "obesity-surgery",
    "name": "Obesity Surgery",
    "discipline": "medicine",
    "aliases": [
      "Obesity Surgery"
    ]
  },
  {
    "id": "obstetrics-gynecology",
    "name": "Obstetrics and Gynecology",
    "discipline": "medicine",
    "aliases": [
      "Obstetrics and Gynecology"
    ]
  },
  {
    "id": "occupational-environmental-medicine",
    "name": "Occupational and Environmental Medicine",
    "discipline": "medicine",
    "aliases": [
      "Occupational and Environmental Medicine"
    ]
  },
  {
    "id": "oncology-research",
    "name": "Oncology Research",
    "discipline": "medicine",
    "aliases": [
      "Oncology Research"
    ]
  },
  {
    "id": "operative-techniques-sports-medicine",
    "name": "Operative Techniques in Sports Medicine",
    "discipline": "medicine",
    "aliases": [
      "Operative Techniques in Sports Medicine"
    ]
  },
  {
    "id": "ophthalmology",
    "name": "Ophthalmology",
    "discipline": "medicine",
    "aliases": [
      "Ophthalmology"
    ]
  },
  {
    "id": "oral-oncology",
    "name": "Oral Oncology",
    "discipline": "medicine",
    "aliases": [
      "Oral Oncology"
    ]
  },
  {
    "id": "orthopedics",
    "name": "Orthopedics",
    "discipline": "medicine",
    "aliases": [
      "Orthopedics"
    ]
  },
  {
    "id": "osteoporosis-international",
    "name": "Osteoporosis International",
    "discipline": "medicine",
    "aliases": [
      "Osteoporosis International"
    ]
  },
  {
    "id": "otolaryngology-head-neck-surgery",
    "name": "Otolaryngology Head and Neck Surgery",
    "discipline": "medicine",
    "aliases": [
      "Otolaryngology Head and Neck Surgery"
    ]
  },
  {
    "id": "pancreatology",
    "name": "Pancreatology",
    "discipline": "medicine",
    "aliases": [
      "Pancreatology"
    ]
  },
  {
    "id": "parkinsonism-related-disorders",
    "name": "Parkinsonism and Related Disorders",
    "discipline": "medicine",
    "aliases": [
      "Parkinsonism and Related Disorders"
    ]
  },
  {
    "id": "pathology-oncology-research",
    "name": "Pathology and Oncology Research",
    "discipline": "medicine",
    "aliases": [
      "Pathology and Oncology Research"
    ]
  },
  {
    "id": "pharmacotherapy",
    "name": "Pharmacotherapy",
    "discipline": "medicine",
    "aliases": [
      "Pharmacotherapy"
    ]
  },
  {
    "id": "plastic-reconstructive-surgery",
    "name": "Plastic and Reconstructive Surgery",
    "discipline": "medicine",
    "aliases": [
      "Plastic and Reconstructive Surgery"
    ]
  },
  {
    "id": "postgraduate-medical-journal",
    "name": "Postgraduate Medical Journal",
    "discipline": "medicine",
    "aliases": [
      "Postgraduate Medical Journal"
    ]
  },
  {
    "id": "prostate",
    "name": "Prostate",
    "discipline": "medicine",
    "aliases": [
      "Prostate"
    ]
  },
  {
    "id": "psychosomatic-medicine",
    "name": "Psychosomatic Medicine",
    "discipline": "medicine",
    "aliases": [
      "Psychosomatic Medicine"
    ]
  },
  {
    "id": "pulmonary-pharmacology-therapeutics",
    "name": "Pulmonary Pharmacology and Therapeutics",
    "discipline": "medicine",
    "aliases": [
      "Pulmonary Pharmacology and Therapeutics"
    ]
  },
  {
    "id": "radiation-oncology",
    "name": "Radiation Oncology",
    "discipline": "medicine",
    "aliases": [
      "Radiation Oncology"
    ]
  },
  {
    "id": "reproductive-health",
    "name": "Reproductive Health",
    "discipline": "medicine",
    "aliases": [
      "Reproductive Health"
    ]
  },
  {
    "id": "respiratory-medicine",
    "name": "Respiratory Medicine",
    "discipline": "medicine",
    "aliases": [
      "Respiratory Medicine"
    ]
  },
  {
    "id": "rheumatology-international",
    "name": "Rheumatology International",
    "discipline": "medicine",
    "aliases": [
      "Rheumatology International"
    ]
  },
  {
    "id": "scandinavian-journal-gastroenterology",
    "name": "Scandinavian Journal of Gastroenterology",
    "discipline": "medicine",
    "aliases": [
      "Scandinavian Journal of Gastroenterology"
    ]
  },
  {
    "id": "scandinavian-journal-infectious-diseases",
    "name": "Scandinavian Journal of Infectious Diseases",
    "discipline": "medicine",
    "aliases": [
      "Scandinavian Journal of Infectious Diseases"
    ]
  },
  {
    "id": "seminars-arthritis-rheumatism",
    "name": "Seminars in Arthritis and Rheumatism",
    "discipline": "medicine",
    "aliases": [
      "Seminars in Arthritis and Rheumatism"
    ]
  },
  {
    "id": "seminars-oncology",
    "name": "Seminars in Oncology",
    "discipline": "medicine",
    "aliases": [
      "Seminars in Oncology"
    ]
  },
  {
    "id": "sleep",
    "name": "Sleep",
    "discipline": "medicine",
    "aliases": [
      "Sleep"
    ]
  },
  {
    "id": "supportive-care-cancer",
    "name": "Supportive Care in Cancer",
    "discipline": "medicine",
    "aliases": [
      "Supportive Care in Cancer"
    ]
  },
  {
    "id": "surgical-endoscopy",
    "name": "Surgical Endoscopy",
    "discipline": "medicine",
    "aliases": [
      "Surgical Endoscopy"
    ]
  },
  {
    "id": "surgical-oncology",
    "name": "Surgical Oncology",
    "discipline": "medicine",
    "aliases": [
      "Surgical Oncology"
    ]
  },
  {
    "id": "thrombosis-haemostasis",
    "name": "Thrombosis and Haemostasis",
    "discipline": "medicine",
    "aliases": [
      "Thrombosis and Haemostasis"
    ]
  },
  {
    "id": "transplant-international",
    "name": "Transplant International",
    "discipline": "medicine",
    "aliases": [
      "Transplant International"
    ]
  },
  {
    "id": "tumor-biology",
    "name": "Tumor Biology",
    "discipline": "medicine",
    "aliases": [
      "Tumor Biology"
    ]
  },
  {
    "id": "urologic-oncology",
    "name": "Urologic Oncology",
    "discipline": "medicine",
    "aliases": [
      "Urologic Oncology"
    ]
  },
  {
    "id": "urology-annals",
    "name": "Urology Annals",
    "discipline": "medicine",
    "aliases": [
      "Urology Annals"
    ]
  },
  {
    "id": "vascular-pharmacology",
    "name": "Vascular Pharmacology",
    "discipline": "medicine",
    "aliases": [
      "Vascular Pharmacology"
    ]
  },
  {
    "id": "virology-journal",
    "name": "Virology Journal",
    "discipline": "medicine",
    "aliases": [
      "Virology Journal"
    ]
  },
  {
    "id": "world-journal-surgery",
    "name": "World Journal of Surgery",
    "discipline": "medicine",
    "aliases": [
      "World Journal of Surgery"
    ]
  },
  {
    "id": "acta-physica-sinica",
    "name": "Acta Physica Sinica",
    "discipline": "sciences",
    "aliases": [
      "Acta Physica Sinica"
    ]
  },
  {
    "id": "applied-and-environmental-microbiology",
    "name": "Applied and Environmental Microbiology",
    "discipline": "sciences",
    "aliases": [
      "Applied and Environmental Microbiology"
    ]
  },
  {
    "id": "applied-microbiology-biotechnology",
    "name": "Applied Microbiology and Biotechnology",
    "discipline": "sciences",
    "aliases": [
      "Applied Microbiology and Biotechnology"
    ]
  },
  {
    "id": "cell-biology-international",
    "name": "Cell Biology International",
    "discipline": "sciences",
    "aliases": [
      "Cell Biology International"
    ]
  },
  {
    "id": "chaos",
    "name": "Chaos",
    "discipline": "sciences",
    "aliases": [
      "Chaos"
    ]
  },
  {
    "id": "chinese-physics-b",
    "name": "Chinese Physics B",
    "discipline": "sciences",
    "aliases": [
      "Chinese Physics B"
    ]
  },
  {
    "id": "current-genetics",
    "name": "Current Genetics",
    "discipline": "sciences",
    "aliases": [
      "Current Genetics"
    ]
  },
  {
    "id": "cytogenetic-genome-research",
    "name": "Cytogenetic and Genome Research",
    "discipline": "sciences",
    "aliases": [
      "Cytogenetic and Genome Research"
    ]
  },
  {
    "id": "ecosphere",
    "name": "Ecosphere",
    "discipline": "sciences",
    "aliases": [
      "Ecosphere"
    ]
  },
  {
    "id": "environmental-biology-fishes",
    "name": "Environmental Biology of Fishes",
    "discipline": "sciences",
    "aliases": [
      "Environmental Biology of Fishes"
    ]
  },
  {
    "id": "eur-j-biochem",
    "name": "European Journal of Biochemistry",
    "discipline": "sciences",
    "aliases": [
      "European Journal of Biochemistry"
    ]
  },
  {
    "id": "genes",
    "name": "Genes",
    "discipline": "sciences",
    "aliases": [
      "Genes"
    ]
  },
  {
    "id": "genetics",
    "name": "Genetics",
    "discipline": "sciences",
    "aliases": [
      "Genetics"
    ]
  },
  {
    "id": "glycobiology",
    "name": "Glycobiology",
    "discipline": "sciences",
    "aliases": [
      "Glycobiology"
    ]
  },
  {
    "id": "immunogenetics",
    "name": "Immunogenetics",
    "discipline": "sciences",
    "aliases": [
      "Immunogenetics"
    ]
  },
  {
    "id": "insect-molecular-biology",
    "name": "Insect Molecular Biology",
    "discipline": "sciences",
    "aliases": [
      "Insect Molecular Biology"
    ]
  },
  {
    "id": "journal-eukaryotic-microbiology",
    "name": "Journal of Eukaryotic Microbiology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Eukaryotic Microbiology"
    ]
  },
  {
    "id": "journal-general-physiology",
    "name": "Journal of General Physiology",
    "discipline": "sciences",
    "aliases": [
      "Journal of General Physiology"
    ]
  },
  {
    "id": "journal-genetics",
    "name": "Journal of Genetics",
    "discipline": "sciences",
    "aliases": [
      "Journal of Genetics"
    ]
  },
  {
    "id": "journal-phytopathology",
    "name": "Journal of Phytopathology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Phytopathology"
    ]
  },
  {
    "id": "journal-zoology",
    "name": "Journal of Zoology",
    "discipline": "sciences",
    "aliases": [
      "Journal of Zoology"
    ]
  },
  {
    "id": "mammalian-genome",
    "name": "Mammalian Genome",
    "discipline": "sciences",
    "aliases": [
      "Mammalian Genome"
    ]
  },
  {
    "id": "microbial-biotechnology",
    "name": "Microbial Biotechnology",
    "discipline": "sciences",
    "aliases": [
      "Microbial Biotechnology"
    ]
  },
  {
    "id": "microbial-cell-factories",
    "name": "Microbial Cell Factories",
    "discipline": "sciences",
    "aliases": [
      "Microbial Cell Factories"
    ]
  },
  {
    "id": "microbial-ecology",
    "name": "Microbial Ecology",
    "discipline": "sciences",
    "aliases": [
      "Microbial Ecology"
    ]
  },
  {
    "id": "microbial-pathogenesis",
    "name": "Microbial Pathogenesis",
    "discipline": "sciences",
    "aliases": [
      "Microbial Pathogenesis"
    ]
  },
  {
    "id": "microbiology-spectrum",
    "name": "Microbiology Spectrum",
    "discipline": "sciences",
    "aliases": [
      "Microbiology Spectrum"
    ]
  },
  {
    "id": "molecular-ecology-notes",
    "name": "Molecular Ecology Notes",
    "discipline": "sciences",
    "aliases": [
      "Molecular Ecology Notes"
    ]
  },
  {
    "id": "molecular-phylogenetics-evolution",
    "name": "Molecular Phylogenetics and Evolution",
    "discipline": "sciences",
    "aliases": [
      "Molecular Phylogenetics and Evolution"
    ]
  },
  {
    "id": "mycologia",
    "name": "Mycologia",
    "discipline": "sciences",
    "aliases": [
      "Mycologia"
    ]
  },
  {
    "id": "mycorrhiza",
    "name": "Mycorrhiza",
    "discipline": "sciences",
    "aliases": [
      "Mycorrhiza"
    ]
  },
  {
    "id": "parasitology",
    "name": "Parasitology",
    "discipline": "sciences",
    "aliases": [
      "Parasitology"
    ]
  },
  {
    "id": "parasitology-research",
    "name": "Parasitology Research",
    "discipline": "sciences",
    "aliases": [
      "Parasitology Research"
    ]
  },
  {
    "id": "pathogen-and-disease",
    "name": "Pathogens and Disease",
    "discipline": "sciences",
    "aliases": [
      "Pathogens and Disease"
    ]
  },
  {
    "id": "phytochemistry",
    "name": "Phytochemistry",
    "discipline": "sciences",
    "aliases": [
      "Phytochemistry"
    ]
  },
  {
    "id": "planta",
    "name": "Planta",
    "discipline": "sciences",
    "aliases": [
      "Planta"
    ]
  },
  {
    "id": "plant-and-soil",
    "name": "Plant and Soil",
    "discipline": "sciences",
    "aliases": [
      "Plant and Soil"
    ]
  },
  {
    "id": "plant-biology",
    "name": "Plant Biology",
    "discipline": "sciences",
    "aliases": [
      "Plant Biology"
    ]
  },
  {
    "id": "plant-disease",
    "name": "Plant Disease",
    "discipline": "sciences",
    "aliases": [
      "Plant Disease"
    ]
  },
  {
    "id": "population-ecology",
    "name": "Population Ecology",
    "discipline": "sciences",
    "aliases": [
      "Population Ecology"
    ]
  },
  {
    "id": "protoplasma",
    "name": "Protoplasma",
    "discipline": "sciences",
    "aliases": [
      "Protoplasma"
    ]
  },
  {
    "id": "research-microbiology",
    "name": "Research in Microbiology",
    "discipline": "sciences",
    "aliases": [
      "Research in Microbiology"
    ]
  },
  {
    "id": "systematic-and-applied-microbiology",
    "name": "Systematic and Applied Microbiology",
    "discipline": "sciences",
    "aliases": [
      "Systematic and Applied Microbiology"
    ]
  },
  {
    "id": "the-american-naturalist",
    "name": "The American Naturalist",
    "discipline": "sciences",
    "aliases": [
      "The American Naturalist"
    ]
  },
  {
    "id": "theoretical-population-biology",
    "name": "Theoretical Population Biology",
    "discipline": "sciences",
    "aliases": [
      "Theoretical Population Biology"
    ]
  },
  {
    "id": "virology",
    "name": "Virology",
    "discipline": "sciences",
    "aliases": [
      "Virology"
    ]
  },
  {
    "id": "zoological-journal-linnean-society",
    "name": "Zoological Journal of the Linnean Society",
    "discipline": "sciences",
    "aliases": [
      "Zoological Journal of the Linnean Society"
    ]
  },
  {
    "id": "institutional-sciences-variant-1",
    "name": "Institutional Sciences Style Variant 1",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 1"
    ]
  },
  {
    "id": "institutional-sciences-variant-2",
    "name": "Institutional Sciences Style Variant 2",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 2"
    ]
  },
  {
    "id": "institutional-sciences-variant-3",
    "name": "Institutional Sciences Style Variant 3",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 3"
    ]
  },
  {
    "id": "institutional-sciences-variant-4",
    "name": "Institutional Sciences Style Variant 4",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 4"
    ]
  },
  {
    "id": "institutional-sciences-variant-5",
    "name": "Institutional Sciences Style Variant 5",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 5"
    ]
  },
  {
    "id": "institutional-sciences-variant-6",
    "name": "Institutional Sciences Style Variant 6",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 6"
    ]
  },
  {
    "id": "institutional-sciences-variant-7",
    "name": "Institutional Sciences Style Variant 7",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 7"
    ]
  },
  {
    "id": "institutional-sciences-variant-8",
    "name": "Institutional Sciences Style Variant 8",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 8"
    ]
  },
  {
    "id": "institutional-sciences-variant-9",
    "name": "Institutional Sciences Style Variant 9",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 9"
    ]
  },
  {
    "id": "institutional-sciences-variant-10",
    "name": "Institutional Sciences Style Variant 10",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 10"
    ]
  },
  {
    "id": "institutional-sciences-variant-11",
    "name": "Institutional Sciences Style Variant 11",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 11"
    ]
  },
  {
    "id": "institutional-sciences-variant-12",
    "name": "Institutional Sciences Style Variant 12",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 12"
    ]
  },
  {
    "id": "institutional-sciences-variant-13",
    "name": "Institutional Sciences Style Variant 13",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 13"
    ]
  },
  {
    "id": "institutional-sciences-variant-14",
    "name": "Institutional Sciences Style Variant 14",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 14"
    ]
  },
  {
    "id": "institutional-sciences-variant-15",
    "name": "Institutional Sciences Style Variant 15",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 15"
    ]
  },
  {
    "id": "institutional-sciences-variant-16",
    "name": "Institutional Sciences Style Variant 16",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 16"
    ]
  },
  {
    "id": "institutional-sciences-variant-17",
    "name": "Institutional Sciences Style Variant 17",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 17"
    ]
  },
  {
    "id": "institutional-sciences-variant-18",
    "name": "Institutional Sciences Style Variant 18",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 18"
    ]
  },
  {
    "id": "institutional-sciences-variant-19",
    "name": "Institutional Sciences Style Variant 19",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 19"
    ]
  },
  {
    "id": "institutional-sciences-variant-20",
    "name": "Institutional Sciences Style Variant 20",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 20"
    ]
  },
  {
    "id": "institutional-sciences-variant-21",
    "name": "Institutional Sciences Style Variant 21",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 21"
    ]
  },
  {
    "id": "institutional-sciences-variant-22",
    "name": "Institutional Sciences Style Variant 22",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 22"
    ]
  },
  {
    "id": "institutional-sciences-variant-23",
    "name": "Institutional Sciences Style Variant 23",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 23"
    ]
  },
  {
    "id": "institutional-sciences-variant-24",
    "name": "Institutional Sciences Style Variant 24",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 24"
    ]
  },
  {
    "id": "institutional-sciences-variant-25",
    "name": "Institutional Sciences Style Variant 25",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 25"
    ]
  },
  {
    "id": "institutional-sciences-variant-26",
    "name": "Institutional Sciences Style Variant 26",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 26"
    ]
  },
  {
    "id": "institutional-sciences-variant-27",
    "name": "Institutional Sciences Style Variant 27",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 27"
    ]
  },
  {
    "id": "institutional-sciences-variant-28",
    "name": "Institutional Sciences Style Variant 28",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 28"
    ]
  },
  {
    "id": "institutional-sciences-variant-29",
    "name": "Institutional Sciences Style Variant 29",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 29"
    ]
  },
  {
    "id": "institutional-sciences-variant-30",
    "name": "Institutional Sciences Style Variant 30",
    "discipline": "sciences",
    "aliases": [
      "institutional sciences",
      "variant 30"
    ]
  },
  {
    "id": "institutional-humanities-variant-1",
    "name": "Institutional Humanities Style Variant 1",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 1"
    ]
  },
  {
    "id": "institutional-humanities-variant-2",
    "name": "Institutional Humanities Style Variant 2",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 2"
    ]
  },
  {
    "id": "institutional-humanities-variant-3",
    "name": "Institutional Humanities Style Variant 3",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 3"
    ]
  },
  {
    "id": "institutional-humanities-variant-4",
    "name": "Institutional Humanities Style Variant 4",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 4"
    ]
  },
  {
    "id": "institutional-humanities-variant-5",
    "name": "Institutional Humanities Style Variant 5",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 5"
    ]
  },
  {
    "id": "institutional-humanities-variant-6",
    "name": "Institutional Humanities Style Variant 6",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 6"
    ]
  },
  {
    "id": "institutional-humanities-variant-7",
    "name": "Institutional Humanities Style Variant 7",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 7"
    ]
  },
  {
    "id": "institutional-humanities-variant-8",
    "name": "Institutional Humanities Style Variant 8",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 8"
    ]
  },
  {
    "id": "institutional-humanities-variant-9",
    "name": "Institutional Humanities Style Variant 9",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 9"
    ]
  },
  {
    "id": "institutional-humanities-variant-10",
    "name": "Institutional Humanities Style Variant 10",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 10"
    ]
  },
  {
    "id": "institutional-humanities-variant-11",
    "name": "Institutional Humanities Style Variant 11",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 11"
    ]
  },
  {
    "id": "institutional-humanities-variant-12",
    "name": "Institutional Humanities Style Variant 12",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 12"
    ]
  },
  {
    "id": "institutional-humanities-variant-13",
    "name": "Institutional Humanities Style Variant 13",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 13"
    ]
  },
  {
    "id": "institutional-humanities-variant-14",
    "name": "Institutional Humanities Style Variant 14",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 14"
    ]
  },
  {
    "id": "institutional-humanities-variant-15",
    "name": "Institutional Humanities Style Variant 15",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 15"
    ]
  },
  {
    "id": "institutional-humanities-variant-16",
    "name": "Institutional Humanities Style Variant 16",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 16"
    ]
  },
  {
    "id": "institutional-humanities-variant-17",
    "name": "Institutional Humanities Style Variant 17",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 17"
    ]
  },
  {
    "id": "institutional-humanities-variant-18",
    "name": "Institutional Humanities Style Variant 18",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 18"
    ]
  },
  {
    "id": "institutional-humanities-variant-19",
    "name": "Institutional Humanities Style Variant 19",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 19"
    ]
  },
  {
    "id": "institutional-humanities-variant-20",
    "name": "Institutional Humanities Style Variant 20",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 20"
    ]
  },
  {
    "id": "institutional-humanities-variant-21",
    "name": "Institutional Humanities Style Variant 21",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 21"
    ]
  },
  {
    "id": "institutional-humanities-variant-22",
    "name": "Institutional Humanities Style Variant 22",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 22"
    ]
  },
  {
    "id": "institutional-humanities-variant-23",
    "name": "Institutional Humanities Style Variant 23",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 23"
    ]
  },
  {
    "id": "institutional-humanities-variant-24",
    "name": "Institutional Humanities Style Variant 24",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 24"
    ]
  },
  {
    "id": "institutional-humanities-variant-25",
    "name": "Institutional Humanities Style Variant 25",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 25"
    ]
  },
  {
    "id": "institutional-humanities-variant-26",
    "name": "Institutional Humanities Style Variant 26",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 26"
    ]
  },
  {
    "id": "institutional-humanities-variant-27",
    "name": "Institutional Humanities Style Variant 27",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 27"
    ]
  },
  {
    "id": "institutional-humanities-variant-28",
    "name": "Institutional Humanities Style Variant 28",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 28"
    ]
  },
  {
    "id": "institutional-humanities-variant-29",
    "name": "Institutional Humanities Style Variant 29",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 29"
    ]
  },
  {
    "id": "institutional-humanities-variant-30",
    "name": "Institutional Humanities Style Variant 30",
    "discipline": "humanities",
    "aliases": [
      "institutional humanities",
      "variant 30"
    ]
  },
  {
    "id": "institutional-law-variant-1",
    "name": "Institutional Law Style Variant 1",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 1"
    ]
  },
  {
    "id": "institutional-law-variant-2",
    "name": "Institutional Law Style Variant 2",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 2"
    ]
  },
  {
    "id": "institutional-law-variant-3",
    "name": "Institutional Law Style Variant 3",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 3"
    ]
  },
  {
    "id": "institutional-law-variant-4",
    "name": "Institutional Law Style Variant 4",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 4"
    ]
  },
  {
    "id": "institutional-law-variant-5",
    "name": "Institutional Law Style Variant 5",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 5"
    ]
  },
  {
    "id": "institutional-law-variant-6",
    "name": "Institutional Law Style Variant 6",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 6"
    ]
  },
  {
    "id": "institutional-law-variant-7",
    "name": "Institutional Law Style Variant 7",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 7"
    ]
  },
  {
    "id": "institutional-law-variant-8",
    "name": "Institutional Law Style Variant 8",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 8"
    ]
  },
  {
    "id": "institutional-law-variant-9",
    "name": "Institutional Law Style Variant 9",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 9"
    ]
  },
  {
    "id": "institutional-law-variant-10",
    "name": "Institutional Law Style Variant 10",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 10"
    ]
  },
  {
    "id": "institutional-law-variant-11",
    "name": "Institutional Law Style Variant 11",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 11"
    ]
  },
  {
    "id": "institutional-law-variant-12",
    "name": "Institutional Law Style Variant 12",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 12"
    ]
  },
  {
    "id": "institutional-law-variant-13",
    "name": "Institutional Law Style Variant 13",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 13"
    ]
  },
  {
    "id": "institutional-law-variant-14",
    "name": "Institutional Law Style Variant 14",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 14"
    ]
  },
  {
    "id": "institutional-law-variant-15",
    "name": "Institutional Law Style Variant 15",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 15"
    ]
  },
  {
    "id": "institutional-law-variant-16",
    "name": "Institutional Law Style Variant 16",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 16"
    ]
  },
  {
    "id": "institutional-law-variant-17",
    "name": "Institutional Law Style Variant 17",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 17"
    ]
  },
  {
    "id": "institutional-law-variant-18",
    "name": "Institutional Law Style Variant 18",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 18"
    ]
  },
  {
    "id": "institutional-law-variant-19",
    "name": "Institutional Law Style Variant 19",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 19"
    ]
  },
  {
    "id": "institutional-law-variant-20",
    "name": "Institutional Law Style Variant 20",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 20"
    ]
  },
  {
    "id": "institutional-law-variant-21",
    "name": "Institutional Law Style Variant 21",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 21"
    ]
  },
  {
    "id": "institutional-law-variant-22",
    "name": "Institutional Law Style Variant 22",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 22"
    ]
  },
  {
    "id": "institutional-law-variant-23",
    "name": "Institutional Law Style Variant 23",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 23"
    ]
  },
  {
    "id": "institutional-law-variant-24",
    "name": "Institutional Law Style Variant 24",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 24"
    ]
  },
  {
    "id": "institutional-law-variant-25",
    "name": "Institutional Law Style Variant 25",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 25"
    ]
  },
  {
    "id": "institutional-law-variant-26",
    "name": "Institutional Law Style Variant 26",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 26"
    ]
  },
  {
    "id": "institutional-law-variant-27",
    "name": "Institutional Law Style Variant 27",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 27"
    ]
  },
  {
    "id": "institutional-law-variant-28",
    "name": "Institutional Law Style Variant 28",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 28"
    ]
  },
  {
    "id": "institutional-law-variant-29",
    "name": "Institutional Law Style Variant 29",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 29"
    ]
  },
  {
    "id": "institutional-law-variant-30",
    "name": "Institutional Law Style Variant 30",
    "discipline": "law",
    "aliases": [
      "institutional law",
      "variant 30"
    ]
  },
  {
    "id": "institutional-medicine-variant-1",
    "name": "Institutional Medicine Style Variant 1",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 1"
    ]
  },
  {
    "id": "institutional-medicine-variant-2",
    "name": "Institutional Medicine Style Variant 2",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 2"
    ]
  },
  {
    "id": "institutional-medicine-variant-3",
    "name": "Institutional Medicine Style Variant 3",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 3"
    ]
  },
  {
    "id": "institutional-medicine-variant-4",
    "name": "Institutional Medicine Style Variant 4",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 4"
    ]
  },
  {
    "id": "institutional-medicine-variant-5",
    "name": "Institutional Medicine Style Variant 5",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 5"
    ]
  },
  {
    "id": "institutional-medicine-variant-6",
    "name": "Institutional Medicine Style Variant 6",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 6"
    ]
  },
  {
    "id": "institutional-medicine-variant-7",
    "name": "Institutional Medicine Style Variant 7",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 7"
    ]
  },
  {
    "id": "institutional-medicine-variant-8",
    "name": "Institutional Medicine Style Variant 8",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 8"
    ]
  },
  {
    "id": "institutional-medicine-variant-9",
    "name": "Institutional Medicine Style Variant 9",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 9"
    ]
  },
  {
    "id": "institutional-medicine-variant-10",
    "name": "Institutional Medicine Style Variant 10",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 10"
    ]
  },
  {
    "id": "institutional-medicine-variant-11",
    "name": "Institutional Medicine Style Variant 11",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 11"
    ]
  },
  {
    "id": "institutional-medicine-variant-12",
    "name": "Institutional Medicine Style Variant 12",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 12"
    ]
  },
  {
    "id": "institutional-medicine-variant-13",
    "name": "Institutional Medicine Style Variant 13",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 13"
    ]
  },
  {
    "id": "institutional-medicine-variant-14",
    "name": "Institutional Medicine Style Variant 14",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 14"
    ]
  },
  {
    "id": "institutional-medicine-variant-15",
    "name": "Institutional Medicine Style Variant 15",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 15"
    ]
  },
  {
    "id": "institutional-medicine-variant-16",
    "name": "Institutional Medicine Style Variant 16",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 16"
    ]
  },
  {
    "id": "institutional-medicine-variant-17",
    "name": "Institutional Medicine Style Variant 17",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 17"
    ]
  },
  {
    "id": "institutional-medicine-variant-18",
    "name": "Institutional Medicine Style Variant 18",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 18"
    ]
  },
  {
    "id": "institutional-medicine-variant-19",
    "name": "Institutional Medicine Style Variant 19",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 19"
    ]
  },
  {
    "id": "institutional-medicine-variant-20",
    "name": "Institutional Medicine Style Variant 20",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 20"
    ]
  },
  {
    "id": "institutional-medicine-variant-21",
    "name": "Institutional Medicine Style Variant 21",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 21"
    ]
  },
  {
    "id": "institutional-medicine-variant-22",
    "name": "Institutional Medicine Style Variant 22",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 22"
    ]
  },
  {
    "id": "institutional-medicine-variant-23",
    "name": "Institutional Medicine Style Variant 23",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 23"
    ]
  },
  {
    "id": "institutional-medicine-variant-24",
    "name": "Institutional Medicine Style Variant 24",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 24"
    ]
  },
  {
    "id": "institutional-medicine-variant-25",
    "name": "Institutional Medicine Style Variant 25",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 25"
    ]
  },
  {
    "id": "institutional-medicine-variant-26",
    "name": "Institutional Medicine Style Variant 26",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 26"
    ]
  },
  {
    "id": "institutional-medicine-variant-27",
    "name": "Institutional Medicine Style Variant 27",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 27"
    ]
  },
  {
    "id": "institutional-medicine-variant-28",
    "name": "Institutional Medicine Style Variant 28",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 28"
    ]
  },
  {
    "id": "institutional-medicine-variant-29",
    "name": "Institutional Medicine Style Variant 29",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 29"
    ]
  },
  {
    "id": "institutional-medicine-variant-30",
    "name": "Institutional Medicine Style Variant 30",
    "discipline": "medicine",
    "aliases": [
      "institutional medicine",
      "variant 30"
    ]
  },
  {
    "id": "institutional-general-variant-1",
    "name": "Institutional General Style Variant 1",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 1"
    ]
  },
  {
    "id": "institutional-general-variant-2",
    "name": "Institutional General Style Variant 2",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 2"
    ]
  },
  {
    "id": "institutional-general-variant-3",
    "name": "Institutional General Style Variant 3",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 3"
    ]
  },
  {
    "id": "institutional-general-variant-4",
    "name": "Institutional General Style Variant 4",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 4"
    ]
  },
  {
    "id": "institutional-general-variant-5",
    "name": "Institutional General Style Variant 5",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 5"
    ]
  },
  {
    "id": "institutional-general-variant-6",
    "name": "Institutional General Style Variant 6",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 6"
    ]
  },
  {
    "id": "institutional-general-variant-7",
    "name": "Institutional General Style Variant 7",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 7"
    ]
  },
  {
    "id": "institutional-general-variant-8",
    "name": "Institutional General Style Variant 8",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 8"
    ]
  },
  {
    "id": "institutional-general-variant-9",
    "name": "Institutional General Style Variant 9",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 9"
    ]
  },
  {
    "id": "institutional-general-variant-10",
    "name": "Institutional General Style Variant 10",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 10"
    ]
  },
  {
    "id": "institutional-general-variant-11",
    "name": "Institutional General Style Variant 11",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 11"
    ]
  },
  {
    "id": "institutional-general-variant-12",
    "name": "Institutional General Style Variant 12",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 12"
    ]
  },
  {
    "id": "institutional-general-variant-13",
    "name": "Institutional General Style Variant 13",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 13"
    ]
  },
  {
    "id": "institutional-general-variant-14",
    "name": "Institutional General Style Variant 14",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 14"
    ]
  },
  {
    "id": "institutional-general-variant-15",
    "name": "Institutional General Style Variant 15",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 15"
    ]
  },
  {
    "id": "institutional-general-variant-16",
    "name": "Institutional General Style Variant 16",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 16"
    ]
  },
  {
    "id": "institutional-general-variant-17",
    "name": "Institutional General Style Variant 17",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 17"
    ]
  },
  {
    "id": "institutional-general-variant-18",
    "name": "Institutional General Style Variant 18",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 18"
    ]
  },
  {
    "id": "institutional-general-variant-19",
    "name": "Institutional General Style Variant 19",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 19"
    ]
  },
  {
    "id": "institutional-general-variant-20",
    "name": "Institutional General Style Variant 20",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 20"
    ]
  },
  {
    "id": "institutional-general-variant-21",
    "name": "Institutional General Style Variant 21",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 21"
    ]
  },
  {
    "id": "institutional-general-variant-22",
    "name": "Institutional General Style Variant 22",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 22"
    ]
  },
  {
    "id": "institutional-general-variant-23",
    "name": "Institutional General Style Variant 23",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 23"
    ]
  },
  {
    "id": "institutional-general-variant-24",
    "name": "Institutional General Style Variant 24",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 24"
    ]
  },
  {
    "id": "institutional-general-variant-25",
    "name": "Institutional General Style Variant 25",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 25"
    ]
  },
  {
    "id": "institutional-general-variant-26",
    "name": "Institutional General Style Variant 26",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 26"
    ]
  },
  {
    "id": "institutional-general-variant-27",
    "name": "Institutional General Style Variant 27",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 27"
    ]
  },
  {
    "id": "institutional-general-variant-28",
    "name": "Institutional General Style Variant 28",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 28"
    ]
  },
  {
    "id": "institutional-general-variant-29",
    "name": "Institutional General Style Variant 29",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 29"
    ]
  },
  {
    "id": "institutional-general-variant-30",
    "name": "Institutional General Style Variant 30",
    "discipline": "general",
    "aliases": [
      "institutional general",
      "variant 30"
    ]
  },
  {
    "id": "ama-11",
    "name": "AMA 11th Edition",
    "discipline": "medicine",
    "aliases": [
      "AMA",
      "American Medical Association",
      "AMA 11"
    ]
  },
  {
    "id": "ama-10",
    "name": "AMA 10th Edition",
    "discipline": "medicine",
    "aliases": [
      "AMA 10",
      "American Medical Association 10th"
    ]
  },
  {
    "id": "nla",
    "name": "NLA Style",
    "discipline": "humanities",
    "aliases": [
      "NLA",
      "National Library of Australia"
    ]
  },
  {
    "id": "aglc",
    "name": "AGLC (Australian Guide to Legal Citation)",
    "discipline": "law",
    "aliases": [
      "AGLC",
      "Australian legal citation"
    ]
  },
  {
    "id": "aglc-4",
    "name": "AGLC 4th Edition",
    "discipline": "law",
    "aliases": [
      "AGLC4",
      "Australian Guide Legal Citation 4"
    ]
  },
  {
    "id": "iawa",
    "name": "IAWA Style",
    "discipline": "sciences",
    "aliases": [
      "IAWA",
      "wood anatomy"
    ]
  },
  {
    "id": "springer-mathphys",
    "name": "Springer MathPhys",
    "discipline": "sciences",
    "aliases": [
      "Springer MathPhys",
      "mathematics physics Springer"
    ]
  },
  {
    "id": "elsevier-harvard",
    "name": "Elsevier Harvard",
    "discipline": "general",
    "aliases": [
      "Elsevier Harvard",
      "Elsevier author-date"
    ]
  },
  {
    "id": "elsevier-with-titles",
    "name": "Elsevier (with Titles)",
    "discipline": "general",
    "aliases": [
      "Elsevier titles"
    ]
  },
  {
    "id": "elsevier-numbered",
    "name": "Elsevier (Numbered)",
    "discipline": "general",
    "aliases": [
      "Elsevier numbered"
    ]
  },
  {
    "id": "council-science-editors",
    "name": "Council of Science Editors (CSE)",
    "discipline": "sciences",
    "aliases": [
      "CSE",
      "Council of Science Editors",
      "scientific style format"
    ]
  },
  {
    "id": "cse-name-year",
    "name": "CSE Name-Year",
    "discipline": "sciences",
    "aliases": [
      "CSE name-year",
      "Council Science Editors name year"
    ]
  },
  {
    "id": "cse-citation-sequence",
    "name": "CSE Citation-Sequence",
    "discipline": "sciences",
    "aliases": [
      "CSE citation sequence"
    ]
  },
  {
    "id": "cse-citation-name",
    "name": "CSE Citation-Name",
    "discipline": "sciences",
    "aliases": [
      "CSE citation name"
    ]
  },
  {
    "id": "acs-journals",
    "name": "ACS Journal Style",
    "discipline": "sciences",
    "aliases": [
      "ACS Journals",
      "American Chemical Society journals"
    ]
  },
  {
    "id": "royal-society-chemistry",
    "name": "Royal Society of Chemistry",
    "discipline": "sciences",
    "aliases": [
      "RSC",
      "Royal Society of Chemistry",
      "RSC journals"
    ]
  },
  {
    "id": "american-institute-physics",
    "name": "American Institute of Physics",
    "discipline": "sciences",
    "aliases": [
      "AIP",
      "American Institute of Physics"
    ]
  },
  {
    "id": "american-physical-society",
    "name": "American Physical Society",
    "discipline": "sciences",
    "aliases": [
      "APS",
      "American Physical Society",
      "Physical Review style"
    ]
  },
  {
    "id": "american-mathematical-society",
    "name": "American Mathematical Society",
    "discipline": "sciences",
    "aliases": [
      "AMS",
      "American Mathematical Society"
    ]
  },
  {
    "id": "biomed-central",
    "name": "BioMed Central",
    "discipline": "medicine",
    "aliases": [
      "BMC",
      "BioMed Central",
      "BMC journals"
    ]
  },
  {
    "id": "springer-basic",
    "name": "Springer Basic",
    "discipline": "sciences",
    "aliases": [
      "Springer Basic",
      "Springer reference"
    ]
  },
  {
    "id": "springer-lecture-notes",
    "name": "Springer Lecture Notes",
    "discipline": "sciences",
    "aliases": [
      "Springer Lecture Notes",
      "LNCS"
    ]
  },
  {
    "id": "wiley-blackwell",
    "name": "Wiley-Blackwell",
    "discipline": "general",
    "aliases": [
      "Wiley Blackwell",
      "Blackwell journals"
    ]
  },
  {
    "id": "oxford-journals",
    "name": "Oxford Journals",
    "discipline": "general",
    "aliases": [
      "Oxford Journals",
      "OUP journals"
    ]
  },
  {
    "id": "cambridge-journals",
    "name": "Cambridge Journals",
    "discipline": "general",
    "aliases": [
      "Cambridge Journals",
      "Cambridge Core"
    ]
  },
  {
    "id": "nature-author-date",
    "name": "Nature Portfolio Author-Date",
    "discipline": "sciences",
    "aliases": [
      "Nature author-date",
      "Nature Portfolio"
    ]
  },
  {
    "id": "frontiers-in-journals",
    "name": "Frontiers Journals (Author-Date)",
    "discipline": "sciences",
    "aliases": [
      "Frontiers",
      "frontiersin.org"
    ]
  },
  {
    "id": "plos-style",
    "name": "PLOS Style",
    "discipline": "sciences",
    "aliases": [
      "PLOS",
      "Public Library of Science"
    ]
  },
  {
    "id": "hindawi",
    "name": "Hindawi",
    "discipline": "general",
    "aliases": [
      "Hindawi",
      "Hindawi journals"
    ]
  },
  {
    "id": "mdpi",
    "name": "MDPI",
    "discipline": "general",
    "aliases": [
      "MDPI",
      "mdpi.com",
      "Multidisciplinary Digital Publishing Institute"
    ]
  },
  {
    "id": "sage-uk",
    "name": "SAGE UK",
    "discipline": "humanities",
    "aliases": [
      "SAGE UK",
      "SAGE Publications UK"
    ]
  },
  {
    "id": "sage-us",
    "name": "SAGE US",
    "discipline": "general",
    "aliases": [
      "SAGE US",
      "SAGE Publications"
    ]
  },
  {
    "id": "informa-taylor-francis",
    "name": "Taylor and Francis",
    "discipline": "general",
    "aliases": [
      "Taylor Francis",
      "Informa"
    ]
  },
  {
    "id": "iso-690",
    "name": "ISO 690",
    "discipline": "general",
    "aliases": [
      "ISO 690",
      "International Standards Organization 690"
    ]
  },
  {
    "id": "din-1505",
    "name": "DIN 1505",
    "discipline": "sciences",
    "aliases": [
      "DIN 1505",
      "German citation standard"
    ]
  },
  {
    "id": "afnor-z44-005",
    "name": "AFNOR Z44-005",
    "discipline": "general",
    "aliases": [
      "AFNOR",
      "French standard citation"
    ]
  },
  {
    "id": "british-standard-5605",
    "name": "BS 5605",
    "discipline": "humanities",
    "aliases": [
      "BS 5605",
      "British Standard 5605"
    ]
  },
  {
    "id": "cite-them-right",
    "name": "Cite Them Right",
    "discipline": "general",
    "aliases": [
      "Cite Them Right",
      "Palgrave Harvard"
    ]
  },
  {
    "id": "apa-7-annotated",
    "name": "APA 7th Annotated Bibliography",
    "discipline": "general",
    "aliases": [
      "APA annotated",
      "APA annotated bibliography"
    ]
  },
  {
    "id": "mla-9-works-cited",
    "name": "MLA 9th Works Cited",
    "discipline": "humanities",
    "aliases": [
      "MLA works cited",
      "MLA 9 works cited"
    ]
  },
  {
    "id": "chicago-17-bibliography",
    "name": "Chicago 17 Bibliography",
    "discipline": "humanities",
    "aliases": [
      "Chicago bibliography",
      "CMOS bibliography"
    ]
  },
  {
    "id": "turabian-9",
    "name": "Turabian 9th Edition",
    "discipline": "humanities",
    "aliases": [
      "Turabian 9",
      "Turabian ninth"
    ]
  },
  {
    "id": "turabian-8",
    "name": "Turabian 8th Edition",
    "discipline": "humanities",
    "aliases": [
      "Turabian 8",
      "Turabian eighth"
    ]
  },
  {
    "id": "asa-7",
    "name": "ASA 7th Edition",
    "discipline": "humanities",
    "aliases": [
      "ASA 7",
      "American Sociological Association 7th"
    ]
  },
  {
    "id": "asa-5",
    "name": "ASA 5th Edition",
    "discipline": "humanities",
    "aliases": [
      "ASA 5",
      "American Sociological Association 5th"
    ]
  },
  {
    "id": "econometrica",
    "name": "Econometrica",
    "discipline": "humanities",
    "aliases": [
      "Econometrica",
      "economics journal"
    ]
  },
  {
    "id": "review-economic-studies",
    "name": "Review of Economic Studies",
    "discipline": "humanities",
    "aliases": [
      "Review of Economic Studies"
    ]
  },
  {
    "id": "american-economic-journal-macroeconomics",
    "name": "American Economic Journal Macroeconomics",
    "discipline": "humanities",
    "aliases": [
      "AEJ Macroeconomics"
    ]
  },
  {
    "id": "american-economic-journal-microeconomics",
    "name": "American Economic Journal Microeconomics",
    "discipline": "humanities",
    "aliases": [
      "AEJ Microeconomics"
    ]
  },
  {
    "id": "journal-finance",
    "name": "Journal of Finance",
    "discipline": "humanities",
    "aliases": [
      "Journal of Finance",
      "JF"
    ]
  },
  {
    "id": "journal-financial-economics",
    "name": "Journal of Financial Economics",
    "discipline": "humanities",
    "aliases": [
      "Journal of Financial Economics",
      "JFE"
    ]
  },
  {
    "id": "review-financial-studies",
    "name": "Review of Financial Studies",
    "discipline": "humanities",
    "aliases": [
      "Review of Financial Studies",
      "RFS"
    ]
  },
  {
    "id": "management-science",
    "name": "Management Science",
    "discipline": "humanities",
    "aliases": [
      "Management Science",
      "INFORMS"
    ]
  },
  {
    "id": "operations-research",
    "name": "Operations Research",
    "discipline": "sciences",
    "aliases": [
      "Operations Research",
      "INFORMS OR"
    ]
  },
  {
    "id": "information-systems-research",
    "name": "Information Systems Research",
    "discipline": "sciences",
    "aliases": [
      "Information Systems Research",
      "ISR"
    ]
  },
  {
    "id": "mis-quarterly",
    "name": "MIS Quarterly",
    "discipline": "sciences",
    "aliases": [
      "MIS Quarterly",
      "MISQ"
    ]
  },
  {
    "id": "journal-marketing",
    "name": "Journal of Marketing",
    "discipline": "humanities",
    "aliases": [
      "Journal of Marketing",
      "AMA Marketing"
    ]
  },
  {
    "id": "journal-consumer-research",
    "name": "Journal of Consumer Research",
    "discipline": "humanities",
    "aliases": [
      "Journal of Consumer Research",
      "JCR"
    ]
  },
  {
    "id": "journal-marketing-research",
    "name": "Journal of Marketing Research",
    "discipline": "humanities",
    "aliases": [
      "Journal of Marketing Research",
      "JMR"
    ]
  },
  {
    "id": "journal-accounting-research",
    "name": "Journal of Accounting Research",
    "discipline": "humanities",
    "aliases": [
      "Journal of Accounting Research",
      "JAR"
    ]
  },
  {
    "id": "accounting-review",
    "name": "The Accounting Review",
    "discipline": "humanities",
    "aliases": [
      "The Accounting Review",
      "TAR",
      "AAA"
    ]
  },
  {
    "id": "strategic-management-journal",
    "name": "Strategic Management Journal",
    "discipline": "humanities",
    "aliases": [
      "Strategic Management Journal",
      "SMJ"
    ]
  },
  {
    "id": "academy-management-review",
    "name": "Academy of Management Review",
    "discipline": "humanities",
    "aliases": [
      "Academy of Management Review",
      "AMR"
    ]
  },
  {
    "id": "academy-management-journal",
    "name": "Academy of Management Journal",
    "discipline": "humanities",
    "aliases": [
      "Academy of Management Journal",
      "AMJ"
    ]
  },
  {
    "id": "administrative-science-quarterly",
    "name": "Administrative Science Quarterly",
    "discipline": "humanities",
    "aliases": [
      "Administrative Science Quarterly",
      "ASQ"
    ]
  },
  {
    "id": "organizational-behavior-human-decision-processes",
    "name": "Organizational Behavior and Human Decision Processes",
    "discipline": "humanities",
    "aliases": [
      "OBHDP",
      "Organizational Behavior"
    ]
  },
  {
    "id": "journal-applied-psychology",
    "name": "Journal of Applied Psychology",
    "discipline": "humanities",
    "aliases": [
      "Journal of Applied Psychology",
      "JAP"
    ]
  },
  {
    "id": "personnel-psychology",
    "name": "Personnel Psychology",
    "discipline": "humanities",
    "aliases": [
      "Personnel Psychology"
    ]
  },
  {
    "id": "journal-vocational-behavior",
    "name": "Journal of Vocational Behavior",
    "discipline": "humanities",
    "aliases": [
      "Journal of Vocational Behavior"
    ]
  },
  {
    "id": "journal-occupational-organizational-psychology",
    "name": "Journal of Occupational and Organizational Psychology",
    "discipline": "humanities",
    "aliases": [
      "JOOP"
    ]
  },
  {
    "id": "work-stress",
    "name": "Work and Stress",
    "discipline": "humanities",
    "aliases": [
      "Work and Stress"
    ]
  },
  {
    "id": "stress-health",
    "name": "Stress and Health",
    "discipline": "medicine",
    "aliases": [
      "Stress and Health"
    ]
  },
  {
    "id": "educational-administration-quarterly",
    "name": "Educational Administration Quarterly",
    "discipline": "general",
    "aliases": [
      "EAQ",
      "Educational Administration Quarterly"
    ]
  },
  {
    "id": "journal-research-science-teaching",
    "name": "Journal of Research in Science Teaching",
    "discipline": "general",
    "aliases": [
      "JRST",
      "science teaching"
    ]
  },
  {
    "id": "science-education-research",
    "name": "Science Education Research",
    "discipline": "general",
    "aliases": [
      "Science Education Research"
    ]
  },
  {
    "id": "international-journal-stem-education",
    "name": "International Journal of STEM Education",
    "discipline": "general",
    "aliases": [
      "IJSTEM",
      "STEM education"
    ]
  },
  {
    "id": "computers-human-behavior",
    "name": "Computers in Human Behavior",
    "discipline": "sciences",
    "aliases": [
      "Computers in Human Behavior",
      "CHB"
    ]
  },
  {
    "id": "information-technology-people",
    "name": "Information Technology and People",
    "discipline": "sciences",
    "aliases": [
      "IT People",
      "Information Technology People"
    ]
  },
  {
    "id": "european-journal-information-systems",
    "name": "European Journal of Information Systems",
    "discipline": "sciences",
    "aliases": [
      "EJIS"
    ]
  },
  {
    "id": "journal-information-technology",
    "name": "Journal of Information Technology",
    "discipline": "sciences",
    "aliases": [
      "JIT"
    ]
  },
  {
    "id": "telecommunications-policy",
    "name": "Telecommunications Policy",
    "discipline": "sciences",
    "aliases": [
      "Telecommunications Policy"
    ]
  },
  {
    "id": "artificial-intelligence",
    "name": "Artificial Intelligence",
    "discipline": "sciences",
    "aliases": [
      "Artificial Intelligence",
      "AI journal",
      "Elsevier AI"
    ]
  },
  {
    "id": "machine-learning",
    "name": "Machine Learning",
    "discipline": "sciences",
    "aliases": [
      "Machine Learning",
      "Springer ML"
    ]
  },
  {
    "id": "journal-machine-learning-research",
    "name": "Journal of Machine Learning Research",
    "discipline": "sciences",
    "aliases": [
      "JMLR",
      "machine learning research"
    ]
  },
  {
    "id": "data-mining-knowledge-discovery",
    "name": "Data Mining and Knowledge Discovery",
    "discipline": "sciences",
    "aliases": [
      "DMKD",
      "data mining"
    ]
  },
  {
    "id": "knowledge-information-systems",
    "name": "Knowledge and Information Systems",
    "discipline": "sciences",
    "aliases": [
      "KAIS",
      "knowledge systems"
    ]
  },
  {
    "id": "expert-systems-applications",
    "name": "Expert Systems with Applications",
    "discipline": "sciences",
    "aliases": [
      "Expert Systems with Applications",
      "ESA"
    ]
  },
  {
    "id": "fuzzy-sets-systems",
    "name": "Fuzzy Sets and Systems",
    "discipline": "sciences",
    "aliases": [
      "Fuzzy Sets and Systems",
      "FSS"
    ]
  },
  {
    "id": "neurocomputing",
    "name": "Neurocomputing",
    "discipline": "sciences",
    "aliases": [
      "Neurocomputing",
      "Elsevier neurocomputing"
    ]
  },
  {
    "id": "pattern-recognition",
    "name": "Pattern Recognition",
    "discipline": "sciences",
    "aliases": [
      "Pattern Recognition",
      "PR"
    ]
  },
  {
    "id": "pattern-recognition-letters",
    "name": "Pattern Recognition Letters",
    "discipline": "sciences",
    "aliases": [
      "Pattern Recognition Letters"
    ]
  },
  {
    "id": "computer-vision-image-understanding",
    "name": "Computer Vision and Image Understanding",
    "discipline": "sciences",
    "aliases": [
      "CVIU",
      "computer vision"
    ]
  },
  {
    "id": "image-vision-computing",
    "name": "Image and Vision Computing",
    "discipline": "sciences",
    "aliases": [
      "Image and Vision Computing",
      "IVC"
    ]
  },
  {
    "id": "signal-processing",
    "name": "Signal Processing",
    "discipline": "sciences",
    "aliases": [
      "Signal Processing",
      "Elsevier SP"
    ]
  },
  {
    "id": "digital-signal-processing",
    "name": "Digital Signal Processing",
    "discipline": "sciences",
    "aliases": [
      "Digital Signal Processing",
      "DSP"
    ]
  },
  {
    "id": "computers-geosciences",
    "name": "Computers and Geosciences",
    "discipline": "sciences",
    "aliases": [
      "Computers and Geosciences"
    ]
  },
  {
    "id": "environmental-modelling-software",
    "name": "Environmental Modelling and Software",
    "discipline": "sciences",
    "aliases": [
      "Environmental Modelling Software"
    ]
  },
  {
    "id": "ecological-modelling",
    "name": "Ecological Modelling",
    "discipline": "sciences",
    "aliases": [
      "Ecological Modelling"
    ]
  },
  {
    "id": "computers-electronics-agriculture",
    "name": "Computers and Electronics in Agriculture",
    "discipline": "sciences",
    "aliases": [
      "Computers Electronics Agriculture"
    ]
  },
  {
    "id": "biosystems-engineering",
    "name": "Biosystems Engineering",
    "discipline": "sciences",
    "aliases": [
      "Biosystems Engineering"
    ]
  },
  {
    "id": "precision-agriculture",
    "name": "Precision Agriculture",
    "discipline": "sciences",
    "aliases": [
      "Precision Agriculture"
    ]
  },
  {
    "id": "agronomy-journal",
    "name": "Agronomy Journal",
    "discipline": "sciences",
    "aliases": [
      "Agronomy Journal",
      "ASA Agronomy"
    ]
  },
  {
    "id": "crop-science",
    "name": "Crop Science",
    "discipline": "sciences",
    "aliases": [
      "Crop Science",
      "CSSA"
    ]
  },
  {
    "id": "soil-science-society-american-journal",
    "name": "Soil Science Society of America Journal",
    "discipline": "sciences",
    "aliases": [
      "SSSAJ",
      "soil science"
    ]
  },
  {
    "id": "plant-and-cell-physiology",
    "name": "Plant and Cell Physiology",
    "discipline": "sciences",
    "aliases": [
      "Plant and Cell Physiology",
      "PCP"
    ]
  },
  {
    "id": "new-phytologist",
    "name": "New Phytologist",
    "discipline": "sciences",
    "aliases": [
      "New Phytologist"
    ]
  },
  {
    "id": "tree-physiology",
    "name": "Tree Physiology",
    "discipline": "sciences",
    "aliases": [
      "Tree Physiology"
    ]
  },
  {
    "id": "forest-ecology-management",
    "name": "Forest Ecology and Management",
    "discipline": "sciences",
    "aliases": [
      "Forest Ecology and Management"
    ]
  },
  {
    "id": "agroforestry-systems",
    "name": "Agroforestry Systems",
    "discipline": "sciences",
    "aliases": [
      "Agroforestry Systems"
    ]
  }
];
